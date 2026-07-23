import { invoke, SeelenCommand, Widget } from "@seelen-ui/lib";
import { currentMonitor } from "@tauri-apps/api/window";

const EXPECTED_ORIGIN = "http://tauri.localhost";
const PORT_START = 43127;
const PORT_COUNT = 9;
const PROTOCOL_VERSION = 2;
const TILE_WIDTH = 128;
const TILE_GAP = 8;
const TILE_ROW_HEIGHT = 130;
const OVERSCAN_ROWS = 3;
const TIME_FORMATTER = new Intl.DateTimeFormat("ko-KR", {
  hour: "2-digit",
  hour12: false,
  minute: "2-digit",
});
const DATE_FORMATTER = new Intl.DateTimeFormat("ko-KR", {
  day: "numeric",
  month: "long",
  weekday: "long",
  year: "numeric",
});

const widget = Widget.self;
const dock = document.getElementById("dock");
const dockFolders = document.getElementById("dock-folders");
const dockLoose = document.getElementById("dock-loose");
const panel = document.getElementById("settings-panel");
const modal = document.getElementById("file-modal");
const list = document.getElementById("file-list");
const spacer = document.getElementById("file-list-spacer");
const rows = document.getElementById("file-list-rows");
const emptyFiles = document.getElementById("file-empty");
const status = document.getElementById("connection-status");
const rootPath = document.getElementById("root-path");
const rootName = document.getElementById("root-name");
const watchStatus = document.getElementById("watch-status");
const statusMessage = document.getElementById("status-message");

let socket = null;
let sessionToken = null;
let httpBaseUrl = null;
let snapshot = null;
let activeFiles = [];
let generation = 0;
let reconnectTimer = null;
let pingTimer = null;
let settingsHoverTimer = null;
let draggedFolderId = null;
const visualUrls = new Map();

await widget.init({
  saveAndRestoreLastRect: false,
  useThemes: false,
});
await fitWidgetToWorkArea();
bindUi();
updateClock();
setInterval(updateClock, 1000);
await widget.ready();
void reconnect("초기 연결");

function bindUi() {
  const trigger = document.getElementById("settings-trigger");
  trigger.addEventListener("pointerenter", () => {
    clearTimeout(settingsHoverTimer);
    settingsHoverTimer = setTimeout(() => {
      openSettings();
    }, 1000);
  });
  trigger.addEventListener("pointerleave", () => clearTimeout(settingsHoverTimer));
  document.getElementById("settings-close").addEventListener("click", () => {
    closeSettings();
  });
  document.getElementById("refresh-button").addEventListener("click", () => {
    sendAuthenticated({ type: "refresh" });
  });
  document.getElementById("root-choose").addEventListener("click", () => {
    sendAuthenticated({ type: "chooseRoot" });
  });
  document.getElementById("modal-close").addEventListener("click", closeModal);
  modal.addEventListener("pointerdown", (event) => {
    if (event.target === modal) {
      closeModal();
    }
  });
  document.addEventListener("pointerdown", (event) => {
    if (
      !panel.hidden
      && !panel.contains(event.target)
      && !trigger.contains(event.target)
    ) {
      closeSettings();
    }
  });
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") {
      return;
    }
    if (!panel.hidden) {
      closeSettings();
    } else if (!modal.hidden) {
      closeModal();
    }
  });
  list.addEventListener("scroll", renderVisibleRows, { passive: true });
  window.addEventListener("resize", () => {
    if (!modal.hidden) {
      updateVirtualGrid();
    }
  });
}

function openSettings() {
  clearTimeout(settingsHoverTimer);
  closeModal();
  panel.hidden = false;
}

function closeSettings() {
  clearTimeout(settingsHoverTimer);
  panel.hidden = true;
}

async function fitWidgetToWorkArea() {
  const monitor = await currentMonitor();
  if (!monitor) {
    return;
  }

  const { position, size } = monitor.workArea;
  await widget.setPosition({
    left: position.x,
    top: position.y,
    right: position.x + size.width,
    bottom: position.y + size.height,
  });
}

function updateClock() {
  const now = new Date();
  document.getElementById("clock-time").textContent = TIME_FORMATTER.format(now);
  document.getElementById("clock-date").textContent = DATE_FORMATTER.format(now);
}

async function reconnect(reason) {
  const currentGeneration = ++generation;
  clearTimeout(reconnectTimer);
  clearInterval(pingTimer);
  closeSocket();
  setConnection(`${reason}…`, "pending");

  try {
    if (window.location.origin !== EXPECTED_ORIGIN) {
      throw new TerminalError(`허용되지 않은 Origin: ${window.location.origin}`);
    }

    const nonceBytes = crypto.getRandomValues(new Uint8Array(32));
    const nonce = encodeBase64Url(nonceBytes);
    const nonceId = encodeBase64Url(
      new Uint8Array(await crypto.subtle.digest("SHA-256", nonceBytes)),
    );
    const companionPath = await resolveCompanionPath();
    await invoke(SeelenCommand.Run, {
      program: companionPath,
      args: [
        "--bootstrap-nonce",
        nonce,
        "--origin",
        window.location.origin,
        "--port-start",
        String(PORT_START),
        "--port-count",
        String(PORT_COUNT),
      ],
      workingDir: companionPath.slice(0, companionPath.lastIndexOf("\\")),
      elevated: false,
    });

    const port = await discoverPort(nonceBytes, nonceId, currentGeneration);
    if (currentGeneration !== generation) {
      return;
    }

    const connection = await openSocket(port, nonce, currentGeneration);
    if (currentGeneration !== generation) {
      connection.socket.close();
      return;
    }

    socket = connection.socket;
    sessionToken = connection.hello.sessionToken;
    httpBaseUrl = connection.hello.httpBaseUrl;
    rootPath.textContent = connection.hello.desktopRoot;
    rootPath.title = connection.hello.desktopRoot;
    updateWatch(connection.hello.watch);
    applySnapshot(connection.hello.snapshot);
    adoptSocket(currentGeneration);
    setConnection("연결됨", "ok");
  } catch (error) {
    if (currentGeneration !== generation) {
      return;
    }

    setConnection("연결 실패", "error");
    statusMessage.textContent = error instanceof Error ? error.message : String(error);
    if (!(error instanceof TerminalError)) {
      reconnectTimer = setTimeout(() => void reconnect("연결 복구"), 2500);
    }
  }
}

async function resolveCompanionPath() {
  const environment = await invoke(SeelenCommand.GetUserEnvs);
  const localAppData = Object.entries(environment).find(
    ([key]) => key.toUpperCase() === "LOCALAPPDATA",
  )?.[1];
  if (!localAppData) {
    throw new Error("LOCALAPPDATA를 확인할 수 없습니다.");
  }

  return `${localAppData}\\WallpaperSeelen\\Wallpaper.Seelen.Companion.exe`;
}

async function discoverPort(nonceBytes, nonceId, currentGeneration) {
  const key = await crypto.subtle.importKey(
    "raw",
    nonceBytes,
    { hash: "SHA-256", name: "HMAC" },
    false,
    ["sign"],
  );
  const deadline = performance.now() + 12_000;
  while (performance.now() < deadline && currentGeneration === generation) {
    for (let offset = 0; offset < PORT_COUNT; offset += 1) {
      const port = PORT_START + offset;
      const challenge = crypto.getRandomValues(new Uint8Array(32));
      try {
        const response = await fetch(
          `http://127.0.0.1:${port}/bootstrap-proof?nonceId=${encodeURIComponent(
            nonceId,
          )}&challenge=${encodeURIComponent(encodeBase64Url(challenge))}`,
          { cache: "no-store", signal: AbortSignal.timeout(500) },
        );
        if (!response.ok) {
          continue;
        }

        const body = await response.json();
        const expected = new Uint8Array(await crypto.subtle.sign("HMAC", key, challenge));
        if (
          body.protocol === PROTOCOL_VERSION &&
          fixedTimeEqual(expected, decodeBase64Url(body.proof))
        ) {
          return port;
        }
      } catch {
        // Empty and unrelated ports are expected.
      }
    }
    await delay(200);
  }

  throw new Error("인증 가능한 Companion을 찾지 못했습니다.");
}

function openSocket(port, nonce, currentGeneration) {
  return new Promise((resolve, reject) => {
    const candidate = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    let settled = false;
    const timeout = setTimeout(() => fail(new Error("WebSocket 연결 시간이 초과되었습니다.")), 5000);
    const fail = (error) => {
      if (settled) return;
      settled = true;
      clearTimeout(timeout);
      candidate.close();
      reject(error);
    };
    candidate.addEventListener("open", () => {
      if (currentGeneration !== generation) {
        fail(new Error("대체된 연결입니다."));
        return;
      }
      candidate.send(JSON.stringify({ type: "hello", protocol: PROTOCOL_VERSION, nonce }));
    }, { once: true });
    candidate.addEventListener("message", (event) => {
      if (settled) return;
      try {
        const hello = JSON.parse(event.data);
        validateHello(hello, port);
        settled = true;
        clearTimeout(timeout);
        resolve({ hello, socket: candidate });
      } catch (error) {
        fail(error);
      }
    }, { once: true });
    candidate.addEventListener("error", () => fail(new Error("WebSocket 연결이 거부되었습니다.")), { once: true });
    candidate.addEventListener("close", () => fail(new Error("hello 전에 연결이 종료되었습니다.")), { once: true });
  });
}

function validateHello(hello, port) {
  const token = decodeBase64Url(hello?.sessionToken);
  if (
    hello?.type !== "helloAck" ||
    hello.protocol !== PROTOCOL_VERSION ||
    token.length !== 32 ||
    hello.httpBaseUrl !== `http://127.0.0.1:${port}` ||
    typeof hello.desktopRoot !== "string" ||
    typeof hello.snapshot !== "object"
  ) {
    throw new TerminalError("Companion 응답 형식이 올바르지 않습니다.");
  }
}

function adoptSocket(currentGeneration) {
  socket.addEventListener("message", (event) => {
    const message = JSON.parse(event.data);
    if (message.type === "snapshot") {
      applySnapshot(message.snapshot);
    } else if (message.type === "setRootAck" && !message.accepted) {
      statusMessage.textContent = "루트 경로를 읽을 수 없습니다.";
    } else if (message.type === "chooseRootAck" && !message.accepted && !message.canceled) {
      statusMessage.textContent = "선택한 루트 경로를 읽을 수 없습니다.";
    } else if (message.type === "error") {
      statusMessage.textContent = `Companion 오류: ${message.code}`;
    }
  });
  socket.addEventListener("close", () => {
    if (currentGeneration === generation) {
      reconnectTimer = setTimeout(() => void reconnect("연결 복구"), 1000);
    }
  });
  pingTimer = setInterval(() => {
    sendAuthenticated({ type: "ping", timestamp: Date.now() });
  }, 5000);
}

function applySnapshot(nextSnapshot) {
  if (!nextSnapshot || !Number.isSafeInteger(nextSnapshot.revision)) {
    return;
  }

  snapshot = nextSnapshot;
  if (snapshot.rootConfigured === false) {
    openSettings();
  }
  pruneVisualUrls();
  rootPath.textContent = snapshot.rootPath || "선택되지 않음";
  rootPath.title = snapshot.rootPath || "선택되지 않음";
  rootName.textContent = snapshot.rootName || "루트 미설정";
  statusMessage.textContent = getSnapshotStatusText(snapshot);
  renderDock();
  if (!modal.hidden) {
    const selected = [...snapshot.folders, snapshot.looseFiles]
      .find((folder) => folder.id === modal.dataset.folderId);
    if (selected) {
      showFolder(selected, true);
    } else {
      closeModal();
    }
  }
}

function getSnapshotStatusText(current) {
  if (current.message) {
    return current.message;
  }
  if (current.state === "ready") {
    const fileCount = [...current.folders, current.looseFiles]
      .reduce((total, folder) => total + folder.files.length, 0);
    return `${current.folders.length.toLocaleString()}개 폴더 · ${fileCount.toLocaleString()}개 파일`;
  }
  return "파일 상태를 확인하고 있습니다.";
}

function pruneVisualUrls() {
  const validKeys = new Set();
  for (const file of [...snapshot.folders, snapshot.looseFiles].flatMap((folder) => folder.files)) {
    validKeys.add(`${file.id}|${file.lastWriteTimeUtc}|${file.iconPath}`);
    if (file.thumbnailPath) {
      validKeys.add(`${file.id}|${file.lastWriteTimeUtc}|${file.thumbnailPath}`);
    }
  }
  for (const [key, url] of visualUrls) {
    if (!validKeys.has(key)) {
      URL.revokeObjectURL(url.url);
      visualUrls.delete(key);
    }
  }
}

function renderDock() {
  dockFolders.replaceChildren();
  dockLoose.replaceChildren();

  if (snapshot.folders.length === 0) {
    const empty = document.createElement("div");
    empty.className = "dock-empty";
    empty.textContent = snapshot.state === "ready" ? "폴더 없음" : "불러오는 중";
    dockFolders.append(empty);
  }

  for (const folder of snapshot.folders) {
    const button = createDockButton(folder);
    bindFolderDrag(button, folder);
    dockFolders.append(button);
  }

  dockLoose.append(createDockButton(snapshot.looseFiles));
  updateDockSelection();
}

function createDockButton(folder) {
  const button = document.createElement("button");
  button.className = "dock-card";
  button.type = "button";
  button.title = folder.isLooseFiles ? "루트 파일" : folder.name;
  button.draggable = !folder.isLooseFiles;
  button.dataset.folderId = folder.id;
  button.setAttribute("aria-pressed", "false");
  button.innerHTML = folder.isLooseFiles
    ? `
      <span class="loose-glyph" aria-hidden="true"><i></i><i></i><i></i></span>
      <span class="dock-name"></span>
      <span class="open-indicator" aria-hidden="true"></span>
    `
    : `
      <span class="folder-glyph" aria-hidden="true">
        <svg viewBox="0 0 58 58" focusable="false">
          <path d="M4 15h18l6-7h24c2.7 0 4 1.7 4 5v34c0 3.3-1.7 5-5 5H7c-3.3 0-5-1.7-5-5V20c0-3.3.7-5 2-5Z" fill="#ECA4C7"/>
          <path d="M7 20h44c3.3 0 5 1.7 5 5v22c0 3.3-1.7 5-5 5H7c-3.3 0-5-1.7-5-5V25c0-3.3 1.7-5 5-5Z" fill="#B7659D"/>
        </svg>
      </span>
      <span class="dock-name"></span>
      <span class="open-indicator" aria-hidden="true"></span>
    `;
  button.querySelector(".dock-name").textContent = folder.name;
  button.addEventListener("click", () => toggleFolder(folder));
  return button;
}

function updateDockSelection() {
  const activeId = modal.hidden ? null : modal.dataset.folderId;
  for (const button of dock.querySelectorAll(".dock-card")) {
    button.setAttribute(
      "aria-pressed",
      String(button.dataset.folderId === activeId),
    );
  }
}

function bindFolderDrag(button, folder) {
  if (folder.isLooseFiles) return;
  button.addEventListener("dragstart", (event) => {
    draggedFolderId = folder.id;
    button.setAttribute("aria-grabbed", "true");
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", folder.id);
  });
  button.addEventListener("dragend", () => {
    draggedFolderId = null;
    button.removeAttribute("aria-grabbed");
    clearDockDropIndicators();
  });
  button.addEventListener("dragover", (event) => {
    if (draggedFolderId && draggedFolderId !== folder.id) {
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      clearDockDropIndicators();
      const bounds = button.getBoundingClientRect();
      const insertAfter = event.clientX >= bounds.left + bounds.width / 2;
      button.classList.add(insertAfter ? "drag-after" : "drag-before");
    }
  });
  button.addEventListener("dragleave", (event) => {
    if (!button.contains(event.relatedTarget)) {
      button.classList.remove("drag-before", "drag-after");
    }
  });
  button.addEventListener("drop", (event) => {
    event.preventDefault();
    clearDockDropIndicators();
    const order = snapshot.folders.map((item) => item.id);
    const source = order.indexOf(draggedFolderId);
    let target = order.indexOf(folder.id);
    if (source < 0 || target < 0 || source === target) return;
    const bounds = button.getBoundingClientRect();
    const insertAfter = event.clientX >= bounds.left + bounds.width / 2;
    const [moved] = order.splice(source, 1);
    target = order.indexOf(folder.id);
    order.splice(target + (insertAfter ? 1 : 0), 0, moved);
    sendAuthenticated({ type: "setFolderOrder", orderedIds: order });
  });
}

function clearDockDropIndicators() {
  for (const card of dockFolders.querySelectorAll(".dock-card")) {
    card.classList.remove("drag-before", "drag-after");
  }
}

function toggleFolder(folder) {
  if (!modal.hidden && modal.dataset.folderId === folder.id) {
    closeModal();
    return;
  }
  showFolder(folder, false);
}

function showFolder(folder, preserveScroll) {
  const previousScrollTop = preserveScroll ? list.scrollTop : 0;
  modal.hidden = false;
  modal.dataset.folderId = folder.id;
  document.getElementById("modal-title").textContent = folder.isLooseFiles
    ? "루트 파일"
    : folder.name;
  document.getElementById("modal-summary").textContent = `${folder.files.length.toLocaleString()}개 파일`;
  activeFiles = folder.files;
  emptyFiles.hidden = activeFiles.length !== 0;
  list.scrollTop = previousScrollTop;
  updateVirtualGrid();
  updateDockSelection();
  list.focus();
}

function closeModal() {
  modal.hidden = true;
  modal.removeAttribute("data-folder-id");
  activeFiles = [];
  rows.replaceChildren();
  emptyFiles.hidden = true;
  updateDockSelection();
}

function updateVirtualGrid() {
  const columnCount = getFileColumnCount();
  const rowCount = Math.ceil(activeFiles.length / columnCount);
  rows.style.setProperty("--file-columns", String(columnCount));
  spacer.style.height = `${rowCount * TILE_ROW_HEIGHT}px`;
  renderVisibleRows();
}

function getFileColumnCount() {
  return Math.max(
    1,
    Math.floor((list.clientWidth + TILE_GAP) / (TILE_WIDTH + TILE_GAP)),
  );
}

function renderVisibleRows() {
  const columnCount = getFileColumnCount();
  const rowCount = Math.ceil(activeFiles.length / columnCount);
  const firstRow = Math.max(
    0,
    Math.floor(list.scrollTop / TILE_ROW_HEIGHT) - OVERSCAN_ROWS,
  );
  const visibleRows = Math.ceil(list.clientHeight / TILE_ROW_HEIGHT)
    + OVERSCAN_ROWS * 2;
  const lastRow = Math.min(rowCount, firstRow + visibleRows);

  rows.replaceChildren();
  rows.style.setProperty("--file-columns", String(columnCount));

  for (let rowIndex = firstRow; rowIndex < lastRow; rowIndex += 1) {
    const row = document.createElement("div");
    row.className = "file-grid-row";
    row.style.top = `${rowIndex * TILE_ROW_HEIGHT}px`;
    const start = rowIndex * columnCount;
    const end = Math.min(activeFiles.length, start + columnCount);

    for (let fileIndex = start; fileIndex < end; fileIndex += 1) {
      const file = activeFiles[fileIndex];
      const tile = document.createElement("div");
      tile.className = "file-tile";
      tile.tabIndex = 0;
      tile.title = file.name;

      const visual = document.createElement("span");
      visual.className = "file-visual";
      const fallback = document.createElement("span");
      fallback.className = "file-extension";
      fallback.textContent = file.extension.replace(".", "").toUpperCase() || "FILE";
      const image = document.createElement("img");
      image.alt = "";
      image.decoding = "async";
      image.hidden = true;
      visual.append(fallback, image);

      const label = document.createElement("span");
      label.className = "file-name";
      label.textContent = file.name;
      tile.append(visual, label);
      row.append(tile);
      void loadVisual(file, { fallback, image, label, tile, visual });
    }

    rows.append(row);
  }
}

async function loadVisual(file, elements, requestedPath = null) {
  const path = requestedPath ?? file.thumbnailPath ?? file.iconPath;
  const cacheKey = `${file.id}|${file.lastWriteTimeUtc}|${path}`;
  if (visualUrls.has(cacheKey)) {
    applyVisual(elements, visualUrls.get(cacheKey));
    return;
  }

  try {
    const response = await fetch(`${httpBaseUrl}${path}`, {
      cache: "force-cache",
      headers: { Authorization: `Bearer ${sessionToken}` },
    });
    if (!response.ok) throw new Error();
    const result = {
      displayName: decodeDisplayName(
        response.headers.get("X-Wallpaper-Display-Name"),
      ),
      presentation: response.headers.get("X-Wallpaper-Presentation"),
      url: URL.createObjectURL(await response.blob()),
    };
    visualUrls.set(cacheKey, result);
    applyVisual(elements, result);
  } catch {
    if (path !== file.iconPath) {
      await loadVisual(file, elements, file.iconPath);
    } else {
      elements.image.hidden = true;
      elements.fallback.hidden = false;
    }
  }
}

function applyVisual(elements, result) {
  if (!elements.tile.isConnected) {
    return;
  }
  elements.visual.classList.toggle(
    "is-fullbleed",
    result.presentation === "fullbleed",
  );
  elements.image.src = result.url;
  elements.image.hidden = false;
  elements.fallback.hidden = true;
  if (result.displayName) {
    elements.label.textContent = result.displayName;
  }
}

function decodeDisplayName(encoded) {
  if (!encoded) {
    return null;
  }
  try {
    return decodeURIComponent(encoded);
  } catch {
    return null;
  }
}

function sendAuthenticated(message) {
  if (socket?.readyState === WebSocket.OPEN && sessionToken) {
    socket.send(JSON.stringify({ ...message, sessionToken }));
  }
}

function updateWatch(watch) {
  const modes = [];
  if (watch?.contentWatching) modes.push("내용");
  if (watch?.parentWatching) modes.push("루트");
  watchStatus.textContent = modes.length > 0 ? `${modes.join("·")} 감시 중` : "감시 불가";
}

function closeSocket() {
  sessionToken = null;
  httpBaseUrl = null;
  if (socket) {
    socket.close();
    socket = null;
  }
}

function setConnection(text, state) {
  status.textContent = text;
  status.dataset.state = state;
}

function encodeBase64Url(bytes) {
  let binary = "";
  for (const value of bytes) binary += String.fromCharCode(value);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

function decodeBase64Url(encoded) {
  if (typeof encoded !== "string" || !/^[A-Za-z0-9_-]+$/u.test(encoded)) {
    return new Uint8Array();
  }
  const standard = encoded.replaceAll("-", "+").replaceAll("_", "/");
  const padded = standard + "=".repeat((4 - (standard.length % 4)) % 4);
  try {
    return Uint8Array.from(atob(padded), (character) => character.charCodeAt(0));
  } catch {
    return new Uint8Array();
  }
}

function fixedTimeEqual(left, right) {
  if (left.length !== right.length) return false;
  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left[index] ^ right[index];
  }
  return difference === 0;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}

class TerminalError extends Error {}
