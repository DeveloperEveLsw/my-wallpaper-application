import { invoke, SeelenCommand, Widget } from "@seelen-ui/lib";

const EXPECTED_ORIGIN = "http://tauri.localhost";
const PORT_START = 43127;
const PORT_COUNT = 9;
const PROTOCOL_VERSION = 2;
const ROW_HEIGHT = 58;
const OVERSCAN = 8;

const widget = Widget.self;
const app = document.getElementById("wallpaper-desktop");
const dock = document.getElementById("dock");
const panel = document.getElementById("settings-panel");
const modal = document.getElementById("file-modal");
const list = document.getElementById("file-list");
const spacer = document.getElementById("file-list-spacer");
const rows = document.getElementById("file-list-rows");
const status = document.getElementById("connection-status");
const rootPath = document.getElementById("root-path");
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

await widget.init({ autoSizeByContent: app });
bindUi();
await widget.ready();
void reconnect("초기 연결");

function bindUi() {
  const trigger = document.getElementById("settings-trigger");
  trigger.addEventListener("pointerenter", () => {
    settingsHoverTimer = setTimeout(() => {
      panel.hidden = false;
    }, 1000);
  });
  trigger.addEventListener("pointerleave", () => clearTimeout(settingsHoverTimer));
  document.getElementById("settings-close").addEventListener("click", () => {
    panel.hidden = true;
  });
  document.getElementById("refresh-button").addEventListener("click", () => {
    sendAuthenticated({ type: "refresh" });
  });
  document.getElementById("root-apply").addEventListener("click", () => {
    const nextRoot = document.getElementById("root-input").value.trim();
    if (nextRoot) {
      sendAuthenticated({ type: "setRoot", rootPath: nextRoot });
    }
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
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && !modal.hidden) {
      closeModal();
    }
  });
  list.addEventListener("scroll", renderVisibleRows, { passive: true });
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
    document.getElementById("root-input").value = connection.hello.desktopRoot;
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
    panel.hidden = false;
  }
  pruneVisualUrls();
  rootPath.textContent = snapshot.rootPath;
  rootPath.title = snapshot.rootPath;
  if (document.activeElement !== document.getElementById("root-input")) {
    document.getElementById("root-input").value = snapshot.rootPath;
  }
  statusMessage.textContent = snapshot.message ?? "";
  renderDock();
  if (!modal.hidden) {
    const selected = [...snapshot.folders, snapshot.looseFiles]
      .find((folder) => folder.id === modal.dataset.folderId);
    if (selected) {
      openFolder(selected);
    } else {
      closeModal();
    }
  }
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
      URL.revokeObjectURL(url);
      visualUrls.delete(key);
    }
  }
}

function renderDock() {
  dock.replaceChildren();
  const folders = [...snapshot.folders, snapshot.looseFiles];
  if (folders.every((folder) => folder.files.length === 0)) {
    const empty = document.createElement("div");
    empty.className = "empty-card";
    empty.textContent = snapshot.state === "ready" ? "표시할 파일이나 폴더가 없습니다." : snapshot.message;
    dock.append(empty);
    return;
  }

  for (const folder of folders) {
    const button = document.createElement("button");
    button.className = "dock-card";
    if (folder.isLooseFiles) {
      button.classList.add("loose-files-card");
    }
    button.type = "button";
    button.title = folder.name;
    button.draggable = !folder.isLooseFiles;
    button.dataset.folderId = folder.id;
    button.innerHTML = `
      <span class="folder-glyph" aria-hidden="true">${folder.isLooseFiles ? "•••" : "▰"}</span>
      <span class="dock-name"></span>
      <span class="dock-count">${folder.files.length}개</span>
    `;
    button.querySelector(".dock-name").textContent = folder.name;
    button.addEventListener("click", () => openFolder(folder));
    bindFolderDrag(button, folder);
    if (folder.isLooseFiles) {
      const separator = document.createElement("span");
      separator.className = "dock-separator";
      separator.setAttribute("aria-hidden", "true");
      dock.append(separator);
    }
    dock.append(button);
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
  });
  button.addEventListener("dragover", (event) => {
    if (draggedFolderId && draggedFolderId !== folder.id) {
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
    }
  });
  button.addEventListener("drop", (event) => {
    event.preventDefault();
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

function openFolder(folder) {
  modal.hidden = false;
  modal.dataset.folderId = folder.id;
  document.getElementById("modal-title").textContent = folder.isLooseFiles
    ? "루트 파일"
    : folder.name;
  document.getElementById("modal-summary").textContent = `${folder.files.length.toLocaleString()}개 파일`;
  activeFiles = folder.files;
  list.scrollTop = 0;
  spacer.style.height = `${activeFiles.length * ROW_HEIGHT}px`;
  renderVisibleRows();
  list.focus();
}

function closeModal() {
  modal.hidden = true;
  modal.removeAttribute("data-folder-id");
  activeFiles = [];
  rows.replaceChildren();
}

function renderVisibleRows() {
  const first = Math.max(0, Math.floor(list.scrollTop / ROW_HEIGHT) - OVERSCAN);
  const visible = Math.ceil(list.clientHeight / ROW_HEIGHT) + OVERSCAN * 2;
  const last = Math.min(activeFiles.length, first + visible);
  rows.replaceChildren();
  for (let index = first; index < last; index += 1) {
    const file = activeFiles[index];
    const row = document.createElement("div");
    row.className = "file-row";
    row.style.top = `${index * ROW_HEIGHT}px`;
    const image = document.createElement("img");
    image.alt = "";
    image.decoding = "async";
    const label = document.createElement("span");
    label.className = "file-name";
    label.textContent = file.name;
    label.title = file.name;
    const metadata = document.createElement("span");
    metadata.className = "file-meta";
    metadata.textContent = formatBytes(file.length);
    row.append(image, label, metadata);
    rows.append(row);
    void loadVisual(file, image);
  }
}

async function loadVisual(file, image) {
  const path = file.thumbnailPath ?? file.iconPath;
  const cacheKey = `${file.id}|${file.lastWriteTimeUtc}|${path}`;
  if (visualUrls.has(cacheKey)) {
    image.src = visualUrls.get(cacheKey);
    return;
  }

  try {
    const response = await fetch(`${httpBaseUrl}${path}`, {
      cache: "force-cache",
      headers: { Authorization: `Bearer ${sessionToken}` },
    });
    if (!response.ok) throw new Error();
    const url = URL.createObjectURL(await response.blob());
    visualUrls.set(cacheKey, url);
    image.src = url;
  } catch {
    if (path !== file.iconPath) {
      const fallback = { ...file, thumbnailPath: null };
      await loadVisual(fallback, image);
    } else {
      image.alt = file.extension.replace(".", "").toUpperCase() || "FILE";
    }
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

function formatBytes(bytes) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 ** 2) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1024 ** 2).toFixed(1)} MB`;
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
