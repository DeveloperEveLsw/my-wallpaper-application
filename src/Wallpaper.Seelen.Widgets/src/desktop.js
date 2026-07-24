import {
  invoke,
  SeelenCommand,
  SeelenEvent,
  Settings,
  subscribe,
  UIColors,
  Widget,
} from "@seelen-ui/lib";
import { currentMonitor } from "@tauri-apps/api/window";
import {
  hasExceededDragThreshold,
  isNameValidationMessage,
  isValidFileMoveDestination,
  placeFloatingPanel,
  reorderIds,
  validateWindowsName,
} from "./interaction-policy.js";

const EXPECTED_ORIGIN = "http://tauri.localhost";
const PORT_START = 43127;
const PORT_COUNT = 9;
const PROTOCOL_VERSION = 5;
const TILE_WIDTH = 128;
const TILE_GAP = 8;
const TILE_ROW_HEIGHT = 130;
const OVERSCAN_ROWS = 3;
const ROOT_SYNC_DELAY = 450;
const POINTER_DRAG_THRESHOLD = 5;
const POINTER_DRAG_GHOST_OFFSET = 16;

const widget = Widget.self;
const dock = document.getElementById("dock");
const dockFolders = document.getElementById("dock-folders");
const dockLoose = document.getElementById("dock-loose");
const errorPanel = document.getElementById("error-panel");
const errorTitle = document.getElementById("error-title");
const errorMessage = document.getElementById("error-message");
const retryButton = document.getElementById("retry-button");
const errorCloseButton = document.getElementById("error-close");
const modal = document.getElementById("file-modal");
const list = document.getElementById("file-list");
const spacer = document.getElementById("file-list-spacer");
const rows = document.getElementById("file-list-rows");
const emptyFiles = document.getElementById("file-empty");
const itemMenu = document.getElementById("item-menu");
const renameDialog = document.getElementById("rename-dialog");
const renameForm = document.getElementById("rename-form");
const renameInput = document.getElementById("rename-input");
const renameError = document.getElementById("rename-error");
const recycleDialog = document.getElementById("recycle-dialog");
const recycleForm = document.getElementById("recycle-form");
const recycleError = document.getElementById("recycle-error");
const moveDialog = document.getElementById("move-dialog");
const moveForm = document.getElementById("move-form");
const moveInput = document.getElementById("move-input");
const moveError = document.getElementById("move-error");
const fileDragGhost = document.getElementById("file-drag-ghost");
const commandStatus = document.getElementById("command-status");

let socket = null;
let sessionToken = null;
let httpBaseUrl = null;
let snapshot = null;
let activeFiles = [];
let generation = 0;
let reconnectTimer = null;
let pingTimer = null;
let rootSyncTimer = null;
let draggedFile = null;
let pointerDragSession = null;
let suppressedPointerClick = null;
let selectedItemId = null;
let contextItem = null;
let contextItemScreenPoint = null;
let renameTarget = null;
let recycleTarget = null;
let pendingMove = null;
let deferredSnapshot = null;
let widgetPhysicalRect = null;
let lastGlobalCursor = null;
let ignoringCursorEvents = false;
let cursorEventChange = Promise.resolve();
let desiredRootConfiguration = {
  customRootPath: "",
  useDefaultDesktop: true,
};
let showErrorMessages = true;
let activeIssue = null;
let activeIssueKey = null;
const visualUrls = new Map();
const issues = new Map();
const dismissedIssueSignatures = new Map();
const pendingCommands = new Map();
const activeShellMenus = new Map();

await widget.init({
  saveAndRestoreLastRect: false,
  useThemes: false,
});
await fitWidgetToWorkArea();
await initializeSettings();
bindUi();
await widget.ready();
await enableLayeredInputRouting();
void reconnect("초기 연결");

function bindUi() {
  retryButton.addEventListener("click", () => {
    activeIssue?.retry?.();
  });
  errorCloseButton.addEventListener("click", dismissActiveIssue);
  document.getElementById("modal-close").addEventListener("click", closeModal);
  modal.addEventListener("pointerdown", (event) => {
    if (event.target === modal) {
      closeModal();
    }
  });
  itemMenu.addEventListener("click", (event) => {
    const button = event.target.closest("[data-item-command]");
    if (button) {
      void handleItemMenuCommand(button.dataset.itemCommand);
    }
  });
  document.addEventListener("pointerdown", (event) => {
    if (!itemMenu.hidden && !itemMenu.contains(event.target)) {
      closeItemMenu();
    }
  });
  document.addEventListener("pointermove", handlePointerDragMove, true);
  document.addEventListener("pointerup", handlePointerDragEnd, true);
  document.addEventListener("pointercancel", handlePointerDragCancel, true);
  document.addEventListener("keydown", (event) => {
    if (event.key !== "Escape") {
      return;
    }

    if (pointerDragSession) {
      event.preventDefault();
      cancelPointerDrag("드래그를 취소했습니다.");
    } else if (!itemMenu.hidden) {
      event.preventDefault();
      closeItemMenu();
    } else if (!renameDialog.hidden) {
      event.preventDefault();
      closeRenameDialog();
    } else if (!recycleDialog.hidden) {
      event.preventDefault();
      closeRecycleDialog();
    } else if (!moveDialog.hidden) {
      event.preventDefault();
      cancelMoveDialog();
    } else if (!modal.hidden) {
      event.preventDefault();
      closeModal();
    }
  });
  renameInput.addEventListener("input", validateRenameInput);
  moveInput.addEventListener("input", validateMoveInput);
  renameForm.addEventListener("submit", (event) => {
    event.preventDefault();
    void submitRename();
  });
  recycleForm.addEventListener("submit", (event) => {
    event.preventDefault();
    void submitRecycle();
  });
  moveForm.addEventListener("submit", (event) => {
    event.preventDefault();
    void submitMove();
  });
  document.getElementById("rename-cancel").addEventListener("click", closeRenameDialog);
  document.getElementById("recycle-cancel").addEventListener("click", closeRecycleDialog);
  document.getElementById("move-cancel").addEventListener("click", cancelMoveDialog);
  renameDialog.addEventListener("pointerdown", (event) => {
    if (event.target === renameDialog) closeRenameDialog();
  });
  recycleDialog.addEventListener("pointerdown", (event) => {
    if (event.target === recycleDialog) closeRecycleDialog();
  });
  moveDialog.addEventListener("pointerdown", (event) => {
    if (event.target === moveDialog) cancelMoveDialog();
  });
  list.addEventListener("scroll", renderVisibleRows, { passive: true });
  window.addEventListener("resize", () => {
    if (!modal.hidden) {
      updateVirtualGrid();
    }
    if (!itemMenu.hidden) {
      closeItemMenu();
    }
  });
  document.addEventListener("contextmenu", (event) => {
    if (isInteractiveElement(event.target)) {
      event.preventDefault();
    }
  });
}

async function fitWidgetToWorkArea() {
  const monitor = await currentMonitor();
  if (!monitor) {
    return;
  }

  const { position, size } = monitor.workArea;
  widgetPhysicalRect = {
    height: size.height,
    width: size.width,
    x: position.x,
    y: position.y,
  };
  await widget.setPosition({
    left: position.x,
    top: position.y,
    right: position.x + size.width,
    bottom: position.y + size.height,
  });
}

async function initializeSettings() {
  try {
    const colors = await UIColors.getAsync();
    colors.setAsCssVariables();
    await UIColors.onChange((nextColors) => {
      nextColors.setAsCssVariables();
    });
  } catch (error) {
    console.warn("Seelen 강조색을 불러오지 못했습니다.", error);
  }

  try {
    const settings = await Settings.getAsync();
    applyWidgetSettings(settings.getCurrentWidgetConfig());
    await Settings.onChange((nextSettings) => {
      applyWidgetSettings(nextSettings.getCurrentWidgetConfig());
      scheduleRootSynchronization();
    });
  } catch (error) {
    setIssue(
      "settings",
      createIssue(
        95,
        "Seelen 설정을 불러오지 못했습니다.",
        error instanceof Error ? error.message : String(error),
        () => globalThis.location.reload(),
      ),
    );
    console.warn("위젯 설정을 불러오지 못했습니다.", error);
  }
}

function applyWidgetSettings(config) {
  showErrorMessages = config.showErrorMessages !== false;
  const customColor = config.useCustomFolderColor
    && typeof config.customFolderColor === "string"
    && CSS.supports("color", config.customFolderColor)
    ? config.customFolderColor
    : null;
  document.documentElement.style.setProperty(
    "--folder-accent-source",
    customColor ?? "var(--system-accent-color, #b7659d)",
  );
  desiredRootConfiguration = {
    customRootPath: typeof config.customRootPath === "string"
      ? config.customRootPath.trim()
      : "",
    useDefaultDesktop: config.useDefaultDesktop !== false,
  };
  renderIssue();
}

async function enableLayeredInputRouting() {
  if (!widgetPhysicalRect) {
    return;
  }

  try {
    await widget.window.setIgnoreCursorEvents(true);
    ignoringCursorEvents = true;
    await subscribe(SeelenEvent.GlobalMouseMove, (event) => {
      routeGlobalCursor(event.payload);
    });
    routeGlobalCursor(await invoke(SeelenCommand.GetMousePosition));
  } catch (error) {
    ignoringCursorEvents = false;
    try {
      await widget.window.setIgnoreCursorEvents(false);
    } catch {
      // Keep the original initialization error as the actionable diagnostic.
    }
    console.warn("바탕화면 입력 투과를 초기화하지 못했습니다.", error);
  }
}

function routeGlobalCursor(payload) {
  if (
    !widgetPhysicalRect
    || !Array.isArray(payload)
    || payload.length !== 2
  ) {
    return;
  }

  const [mouseX, mouseY] = payload;
  lastGlobalCursor = [mouseX, mouseY];
  if (pointerDragSession) {
    setCursorEventsIgnored(false);
    return;
  }

  const {
    height,
    width,
    x: windowX,
    y: windowY,
  } = widgetPhysicalRect;
  const isInsideWindow = mouseX >= windowX
    && mouseX <= windowX + width
    && mouseY >= windowY
    && mouseY <= windowY + height;
  if (!isInsideWindow) {
    setCursorEventsIgnored(true);
    return;
  }

  const localX = (mouseX - windowX) / globalThis.devicePixelRatio;
  const localY = (mouseY - windowY) / globalThis.devicePixelRatio;
  const element = document.elementFromPoint(localX, localY);
  setCursorEventsIgnored(!isInteractiveElement(element));
}

function rerouteLastCursor() {
  if (lastGlobalCursor) {
    queueMicrotask(() => routeGlobalCursor(lastGlobalCursor));
  }
}

function isInteractiveElement(element) {
  return element instanceof Element
    && element.closest(
      "#dock, #error-panel, #item-menu, .file-modal, .dialog-layer",
    ) !== null;
}

function setCursorEventsIgnored(ignored) {
  if (ignoringCursorEvents === ignored) {
    return;
  }

  ignoringCursorEvents = ignored;
  cursorEventChange = cursorEventChange
    .then(() => widget.window.setIgnoreCursorEvents(ignored))
    .catch((error) => {
      console.warn("위젯 입력 투과 상태를 바꾸지 못했습니다.", error);
    });
}

function createIssue(priority, title, message, retry) {
  return { message, priority, retry, title };
}

function issueSignature(issue) {
  return `${issue.title}\u0000${issue.message}`;
}

function setIssue(key, issue) {
  if (issue) {
    const dismissedSignature = dismissedIssueSignatures.get(key);
    if (dismissedSignature && dismissedSignature !== issueSignature(issue)) {
      dismissedIssueSignatures.delete(key);
    }
    issues.set(key, issue);
  } else {
    issues.delete(key);
    dismissedIssueSignatures.delete(key);
  }

  renderIssue();
}

function dismissActiveIssue() {
  if (!activeIssueKey || !activeIssue) {
    return;
  }

  dismissedIssueSignatures.set(activeIssueKey, issueSignature(activeIssue));
  renderIssue();
}

function renderIssue() {
  const nextEntry = [...issues.entries()]
    .filter(
      ([key, issue]) =>
        dismissedIssueSignatures.get(key) !== issueSignature(issue),
    )
    .sort((left, right) => right[1].priority - left[1].priority)[0] ?? null;
  activeIssueKey = nextEntry?.[0] ?? null;
  activeIssue = nextEntry?.[1] ?? null;
  if (!activeIssue || !showErrorMessages) {
    errorPanel.hidden = true;
    rerouteLastCursor();
    return;
  }

  errorTitle.textContent = activeIssue.title;
  errorMessage.textContent = activeIssue.message;
  retryButton.hidden = typeof activeIssue.retry !== "function";
  errorPanel.hidden = false;
  rerouteLastCursor();
}

function scheduleRootSynchronization() {
  clearTimeout(rootSyncTimer);
  rootSyncTimer = setTimeout(() => {
    rootSyncTimer = null;
    synchronizeRootConfiguration();
  }, ROOT_SYNC_DELAY);
}

function synchronizeRootConfiguration(force = false) {
  if (!sessionToken || socket?.readyState !== WebSocket.OPEN) {
    return;
  }

  if (desiredRootConfiguration.useDefaultDesktop) {
    if (!force && snapshot?.rootConfigured === false) {
      setIssue("root-configuration", null);
      return;
    }

    sendAuthenticated({ type: "useDefaultRoot" });
    return;
  }

  const path = desiredRootConfiguration.customRootPath;
  if (!path) {
    setIssue(
      "root-configuration",
      createIssue(
        90,
        "사용자 지정 루트 경로가 비어 있습니다.",
        "Seelen의 Wallpaper 데스크톱 설정에서 Windows 절대 경로를 입력하세요.",
        () => synchronizeRootConfiguration(true),
      ),
    );
    return;
  }

  if (!force && snapshot?.rootConfigured && sameWindowsPath(snapshot.rootPath, path)) {
    setIssue("root-configuration", null);
    return;
  }

  sendAuthenticated({ type: "setRoot", rootPath: path });
}

function sameWindowsPath(left, right) {
  return normalizeWindowsPath(left) === normalizeWindowsPath(right);
}

function normalizeWindowsPath(path) {
  return typeof path === "string"
    ? path.trim().replace(/[\\/]+$/u, "").toLocaleLowerCase("en-US")
    : "";
}

function retryRefresh() {
  if (!sendAuthenticated({ type: "refresh" })) {
    void reconnect("수동 재연결");
  }
}

async function reconnect(reason) {
  const currentGeneration = ++generation;
  clearTimeout(reconnectTimer);
  clearInterval(pingTimer);
  closeSocket();
  setIssue("connection", null);

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
    updateWatch(connection.hello.watch);
    applySnapshot(connection.hello.snapshot);
    adoptSocket(currentGeneration);
    synchronizeRootConfiguration();
  } catch (error) {
    if (currentGeneration !== generation) {
      return;
    }

    setIssue(
      "connection",
      createIssue(
        100,
        "Companion에 연결하지 못했습니다.",
        error instanceof Error ? error.message : String(error),
        () => void reconnect("수동 재연결"),
      ),
    );
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
      updateWatch(message.watch);
      applySnapshot(message.snapshot);
    } else if (message.type === "setRootAck" && !message.accepted) {
      if (
        !desiredRootConfiguration.useDefaultDesktop
        && sameWindowsPath(message.rootPath, desiredRootConfiguration.customRootPath)
      ) {
        setIssue(
          "root-configuration",
          createIssue(
            90,
            "루트 폴더를 사용할 수 없습니다.",
            "Seelen 설정의 사용자 지정 루트 경로와 접근 권한을 확인하세요.",
            () => synchronizeRootConfiguration(true),
          ),
        );
      }
    } else if (message.type === "useDefaultRootAck" && !message.accepted) {
      if (desiredRootConfiguration.useDefaultDesktop) {
        setIssue(
          "root-configuration",
          createIssue(
            90,
            "기본 Desktop 폴더를 사용할 수 없습니다.",
            "Windows Desktop 폴더의 위치와 접근 권한을 확인하세요.",
            () => synchronizeRootConfiguration(true),
          ),
        );
      }
    } else if (message.type === "itemCommandAck") {
      settlePendingCommand(message);
    } else if (message.type === "shellMenuPrepareAck") {
      settlePendingCommand(message);
    } else if (message.type === "shellMenuCompleted") {
      handleShellMenuCompletion(message);
    } else if (message.type === "error") {
      setIssue(
        "command",
        createIssue(
          60,
          "Companion 명령을 처리하지 못했습니다.",
          `오류 코드: ${message.code}`,
          () => {
            setIssue("command", null);
            retryRefresh();
          },
        ),
      );
    }
  });
  socket.addEventListener("close", () => {
    if (currentGeneration === generation) {
      setIssue(
        "connection",
        createIssue(
          100,
          "Companion 연결이 끊겼습니다.",
          "자동으로 다시 연결하는 중입니다.",
          () => void reconnect("수동 재연결"),
        ),
      );
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

  if (shouldDeferSnapshot()) {
    deferredSnapshot = nextSnapshot;
    return;
  }

  applySnapshotNow(nextSnapshot);
}

function applySnapshotNow(nextSnapshot) {
  snapshot = nextSnapshot;
  pruneVisualUrls();
  updateSnapshotIssue(snapshot);
  if (
    desiredRootConfiguration.useDefaultDesktop
      ? snapshot.rootConfigured === false
      : snapshot.rootConfigured
        && sameWindowsPath(snapshot.rootPath, desiredRootConfiguration.customRootPath)
  ) {
    setIssue("root-configuration", null);
  }
  if (selectedItemId && !findProjectedFile(selectedItemId)) {
    selectedItemId = null;
  }
  if (contextItem && !findProjectedItem(contextItem.id)) {
    closeItemMenu();
  }
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

function shouldDeferSnapshot() {
  return pointerDragSession
    || !renameDialog.hidden
    || !recycleDialog.hidden
    || !moveDialog.hidden;
}

function flushDeferredSnapshot() {
  if (!deferredSnapshot || shouldDeferSnapshot()) {
    return;
  }

  const pending = deferredSnapshot;
  deferredSnapshot = null;
  applySnapshotNow(pending);
}

function updateSnapshotIssue(current) {
  if (current.state === "ready" || current.state === "loading") {
    setIssue("snapshot", null);
    return;
  }

  const descriptions = {
    "access-denied": [
      "루트 폴더에 접근할 수 없습니다.",
      "Seelen 설정의 루트 경로와 Windows 폴더 권한을 확인하세요.",
    ],
    "root-missing": [
      "루트 폴더를 찾을 수 없습니다.",
      "폴더를 복원하거나 Seelen 설정에서 다른 루트 경로를 입력하세요.",
    ],
    warning: [
      "일부 항목을 읽지 못했습니다.",
      current.message || "읽을 수 없는 항목을 확인한 뒤 다시 시도하세요.",
    ],
  };
  const [title, message] = descriptions[current.state] ?? [
    "Desktop 내용을 불러오지 못했습니다.",
    current.message || "잠시 후 다시 시도하세요.",
  ];
  setIssue("snapshot", createIssue(80, title, message, retryRefresh));
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
    empty.textContent = snapshot.state === "ready" || snapshot.state === "warning"
      ? "폴더 없음"
      : "루트 오류";
    dockFolders.append(empty);
  }

  for (const folder of snapshot.folders) {
    const button = createDockButton(folder);
    bindFolderDrag(button, folder);
    dockFolders.append(button);
  }

  const looseButton = createDockButton(snapshot.looseFiles);
  dockLoose.append(looseButton);
  updateFileDropTargets();
  updateDockSelection();
}

function createDockButton(folder) {
  const button = document.createElement("button");
  button.className = "dock-card";
  button.type = "button";
  button.title = folder.isLooseFiles ? "루트 파일" : folder.name;
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
          <path class="folder-glyph-back" d="M4 15h18l6-7h24c2.7 0 4 1.7 4 5v34c0 3.3-1.7 5-5 5H7c-3.3 0-5-1.7-5-5V20c0-3.3.7-5 2-5Z"/>
          <path class="folder-glyph-front" d="M7 20h44c3.3 0 5 1.7 5 5v22c0 3.3-1.7 5-5 5H7c-3.3 0-5-1.7-5-5V25c0-3.3 1.7-5 5-5Z"/>
        </svg>
      </span>
      <span class="dock-name"></span>
      <span class="open-indicator" aria-hidden="true"></span>
    `;
  button.querySelector(".dock-name").textContent = folder.isLooseFiles
    ? "루트"
    : folder.name;
  button.addEventListener("click", (event) => {
    if (consumeSuppressedPointerClick(folder.id)) {
      event.preventDefault();
      event.stopPropagation();
      return;
    }
    toggleFolder(folder);
  });
  if (!folder.isLooseFiles) {
    button.addEventListener("contextmenu", (event) => {
      event.preventDefault();
      event.stopPropagation();
      openItemMenu(createFolderItem(folder), event.clientX, event.clientY);
    });
  }
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
  if (folder.isLooseFiles) {
    return;
  }

  button.addEventListener("pointerdown", (event) => {
    beginPointerDrag(event, {
      folder,
      kind: "folder",
      itemId: folder.id,
    });
  });
}

function clearDockDropIndicators() {
  for (const card of dockFolders.querySelectorAll(".dock-card")) {
    card.classList.remove("drag-before", "drag-after");
  }
}

function beginPointerDrag(event, descriptor) {
  if (
    event.button !== 0
    || event.isPrimary === false
    || pointerDragSession
  ) {
    return;
  }

  const sourceElement = event.currentTarget;
  pointerDragSession = {
    ...descriptor,
    activated: false,
    insertAfter: false,
    lastX: event.clientX,
    lastY: event.clientY,
    pointerId: event.pointerId,
    sourceElement,
    startX: event.clientX,
    startY: event.clientY,
    targetId: null,
  };
  setCursorEventsIgnored(false);
  try {
    sourceElement.setPointerCapture(event.pointerId);
    sourceElement.addEventListener(
      "lostpointercapture",
      handlePointerDragCaptureLost,
      { once: true },
    );
  } catch (error) {
    pointerDragSession = null;
    rerouteLastCursor();
    console.warn("드래그 포인터를 캡처하지 못했습니다.", error);
  }
}

function handlePointerDragMove(event) {
  const session = pointerDragSession;
  if (!session || event.pointerId !== session.pointerId) {
    return;
  }

  session.lastX = event.clientX;
  session.lastY = event.clientY;
  if (
    !session.activated
    && !hasExceededDragThreshold(
      session.startX,
      session.startY,
      event.clientX,
      event.clientY,
      POINTER_DRAG_THRESHOLD,
    )
  ) {
    return;
  }

  if (!session.activated) {
    activatePointerDrag(session);
  }

  event.preventDefault();
  event.stopPropagation();
  updatePointerDragAt(session, event.clientX, event.clientY);
}

function handlePointerDragEnd(event) {
  if (!pointerDragSession || event.pointerId !== pointerDragSession.pointerId) {
    return;
  }

  finishPointerDrag({
    cancelled: false,
    clientX: event.clientX,
    clientY: event.clientY,
  });
}

function handlePointerDragCancel(event) {
  if (!pointerDragSession || event.pointerId !== pointerDragSession.pointerId) {
    return;
  }

  finishPointerDrag({
    cancelled: true,
    reason: "드래그를 취소했습니다.",
  });
}

function handlePointerDragCaptureLost(event) {
  if (!pointerDragSession || event.pointerId !== pointerDragSession.pointerId) {
    return;
  }

  finishPointerDrag({
    cancelled: true,
    reason: "포인터 연결이 끊겨 드래그를 취소했습니다.",
  });
}

function cancelPointerDrag(reason) {
  if (!pointerDragSession) {
    return;
  }

  finishPointerDrag({ cancelled: true, reason });
}

function activatePointerDrag(session) {
  session.activated = true;
  closeItemMenu();
  document.body.classList.add("is-pointer-dragging");
  session.sourceElement.classList.add("is-pointer-drag-source");
  session.sourceElement.setAttribute("aria-grabbed", "true");
  if (session.kind === "folder") {
    announceCommand(`${session.folder.name} 순서 변경 시작`);
    return;
  }

  draggedFile = session.file;
  selectFile(session.file.id);
  fileDragGhost.textContent = session.file.name;
  fileDragGhost.hidden = false;
  updateFileDropTargets();
  announceCommand(`${session.file.name} 이동 시작`);
}

function updatePointerDragAt(session, clientX, clientY) {
  if (session.kind === "folder") {
    updateFolderPointerDragAt(session, clientX, clientY);
    return;
  }

  positionFileDragGhost(clientX, clientY);
  updateFilePointerDragAt(session, clientX, clientY);
}

function updateFolderPointerDragAt(session, clientX, clientY) {
  clearDockDropIndicators();
  session.targetId = null;
  const card = findDockCardAtPoint(clientX, clientY);
  if (
    !card
    || !dockFolders.contains(card)
    || card.dataset.folderId === session.folder.id
  ) {
    return;
  }

  const bounds = card.getBoundingClientRect();
  session.targetId = card.dataset.folderId;
  session.insertAfter = clientX >= bounds.left + bounds.width / 2;
  card.classList.add(session.insertAfter ? "drag-after" : "drag-before");
}

function updateFilePointerDragAt(session, clientX, clientY) {
  for (const card of dock.querySelectorAll(".dock-card")) {
    card.classList.remove("file-drop-hover");
  }

  session.targetId = null;
  const card = findDockCardAtPoint(clientX, clientY);
  if (!card) {
    return;
  }

  session.targetId = card.dataset.folderId;
  card.classList.add("file-drop-hover");
}

function findDockCardAtPoint(clientX, clientY) {
  const element = document.elementFromPoint(clientX, clientY);
  const card = element instanceof Element
    ? element.closest(".dock-card")
    : null;
  return card && dock.contains(card) ? card : null;
}

function positionFileDragGhost(clientX, clientY) {
  const position = placeFloatingPanel(
    clientX + POINTER_DRAG_GHOST_OFFSET,
    clientY + POINTER_DRAG_GHOST_OFFSET,
    fileDragGhost.offsetWidth,
    fileDragGhost.offsetHeight,
    globalThis.innerWidth,
    globalThis.innerHeight,
  );
  fileDragGhost.style.left = `${position.left}px`;
  fileDragGhost.style.top = `${position.top}px`;
}

function finishPointerDrag({
  cancelled,
  clientX = null,
  clientY = null,
  reason = null,
}) {
  const session = pointerDragSession;
  if (!session) {
    return;
  }

  if (
    session.activated
    && !cancelled
    && Number.isFinite(clientX)
    && Number.isFinite(clientY)
  ) {
    updatePointerDragAt(session, clientX, clientY);
  }

  const targetId = session.targetId;
  const insertAfter = session.insertAfter;
  const orderedIds = session.kind === "folder" && session.activated && targetId
    ? reorderIds(
      snapshot.folders.map((folder) => folder.id),
      session.folder.id,
      targetId,
      insertAfter,
    )
    : null;
  const destination = session.kind === "file" && session.activated && targetId
    ? findProjectedFolder(targetId)
    : null;

  pointerDragSession = null;
  if (session.sourceElement.hasPointerCapture?.(session.pointerId)) {
    session.sourceElement.releasePointerCapture(session.pointerId);
  }
  document.body.classList.remove("is-pointer-dragging");
  session.sourceElement.classList.remove("is-pointer-drag-source");
  session.sourceElement.removeAttribute("aria-grabbed");
  clearDockDropIndicators();
  clearFileDropTargets();
  fileDragGhost.hidden = true;
  fileDragGhost.style.removeProperty("left");
  fileDragGhost.style.removeProperty("top");
  draggedFile = null;

  if (session.activated) {
    suppressPointerClick(session.itemId);
    if (session.kind === "file") {
      requestAnimationFrame(() => {
        if (!pointerDragSession) {
          renderVisibleRows();
        }
      });
    }
  }

  if (session.activated && cancelled) {
    if (session.kind === "file") {
      retryRefresh();
    }
    announceCommand(reason || "드래그를 취소했습니다.");
  } else if (session.kind === "folder" && orderedIds) {
    sendAuthenticated({ type: "setFolderOrder", orderedIds });
    announceCommand(`${session.folder.name} 순서를 변경했습니다.`);
  } else if (session.kind === "folder" && session.activated) {
    announceCommand("폴더 순서 변경을 취소했습니다.");
  } else if (session.kind === "file" && session.activated && destination) {
    if (isValidFileMoveDestination(session.file.sourceFolderId, destination.id)) {
      void prepareFileMove(session.file, destination);
    } else {
      retryRefresh();
      announceCommand("파일이 이미 선택한 카드에 있습니다.");
    }
  } else if (session.kind === "file" && session.activated) {
    retryRefresh();
    announceCommand("파일 이동을 취소했습니다.");
  }

  flushDeferredSnapshot();
  rerouteLastCursor();
}

function findProjectedFolder(folderId) {
  if (!snapshot || !folderId) {
    return null;
  }

  return [...snapshot.folders, snapshot.looseFiles]
    .find((folder) => folder.id === folderId) ?? null;
}

function suppressPointerClick(itemId) {
  suppressedPointerClick = {
    expiresAt: performance.now() + 750,
    itemId,
  };
}

function consumeSuppressedPointerClick(itemId) {
  if (!suppressedPointerClick) {
    return false;
  }

  const suppressed = suppressedPointerClick;
  suppressedPointerClick = null;
  return suppressed.itemId === itemId
    && performance.now() <= suppressed.expiresAt;
}

function updateFileDropTargets() {
  for (const card of dock.querySelectorAll(".dock-card")) {
    const valid = draggedFile && isValidFileMoveDestination(
      draggedFile.sourceFolderId,
      card.dataset.folderId,
    );
    card.classList.toggle("file-drop-valid", Boolean(valid));
    card.classList.toggle("file-drop-invalid", Boolean(draggedFile) && !valid);
    if (!draggedFile) {
      card.classList.remove("file-drop-hover");
    }
  }
}

function clearFileDropTargets() {
  for (const card of dock.querySelectorAll(".dock-card")) {
    card.classList.remove(
      "file-drop-valid",
      "file-drop-invalid",
      "file-drop-hover",
    );
  }
}

function toggleFolder(folder) {
  closeItemMenu();
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
  if (!activeFiles.some((file) => file.id === selectedItemId)) {
    selectedItemId = null;
  }
  emptyFiles.hidden = activeFiles.length !== 0;
  list.scrollTop = previousScrollTop;
  updateVirtualGrid();
  updateDockSelection();
  list.focus();
  rerouteLastCursor();
}

function closeModal() {
  closeItemMenu();
  modal.hidden = true;
  modal.removeAttribute("data-folder-id");
  activeFiles = [];
  selectedItemId = null;
  rows.replaceChildren();
  emptyFiles.hidden = true;
  updateDockSelection();
  rerouteLastCursor();
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
  if (pointerDragSession?.kind === "file") {
    return;
  }

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
    const visualLoads = [];
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
      tile.dataset.itemId = file.id;
      tile.setAttribute("role", "option");
      tile.setAttribute("aria-selected", String(file.id === selectedItemId));
      tile.classList.toggle("is-selected", file.id === selectedItemId);
      tile.addEventListener("click", (event) => {
        if (consumeSuppressedPointerClick(file.id)) {
          event.preventDefault();
          event.stopPropagation();
          return;
        }
        selectFile(file.id);
      });
      tile.addEventListener("dblclick", (event) => {
        event.preventDefault();
        void executeImmediateItemCommand(
          "open",
          createFileItem(file, modal.dataset.folderId),
        );
      });
      tile.addEventListener("contextmenu", (event) => {
        event.preventDefault();
        event.stopPropagation();
        selectFile(file.id);
        openItemMenu(
          createFileItem(file, modal.dataset.folderId),
          event.clientX,
          event.clientY,
        );
      });
      tile.addEventListener("keydown", (event) => {
        if (event.key === "Enter") {
          event.preventDefault();
          void executeImmediateItemCommand(
            "open",
            createFileItem(file, modal.dataset.folderId),
          );
        } else if (event.key === "F10" && event.shiftKey) {
          event.preventDefault();
          const bounds = tile.getBoundingClientRect();
          openItemMenu(
            createFileItem(file, modal.dataset.folderId),
            bounds.left + Math.min(24, bounds.width / 2),
            bounds.top + Math.min(24, bounds.height / 2),
          );
        }
      });
      bindFileDrag(tile, file, modal.dataset.folderId);

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
      label.textContent = getFileDisplayName(file);
      tile.append(visual, label);
      row.append(tile);
      visualLoads.push([file, { fallback, image, label, tile, visual }]);
    }

    rows.append(row);
    for (const [file, elements] of visualLoads) {
      void loadVisual(file, elements);
    }
  }
}

function selectFile(fileId) {
  selectedItemId = fileId;
  for (const tile of rows.querySelectorAll(".file-tile")) {
    const selected = tile.dataset.itemId === fileId;
    tile.classList.toggle("is-selected", selected);
    tile.setAttribute("aria-selected", String(selected));
  }
}

function bindFileDrag(tile, file, sourceFolderId) {
  tile.addEventListener("pointerdown", (event) => {
    beginPointerDrag(event, {
      file: createFileItem(file, sourceFolderId),
      itemId: file.id,
      kind: "file",
    });
  });
}

function createFileItem(file, sourceFolderId) {
  return {
    id: file.id,
    kind: "file",
    name: file.name,
    sourceFolderId,
  };
}

function createFolderItem(folder) {
  return {
    id: folder.id,
    kind: "folder",
    name: folder.name,
    sourceFolderId: null,
  };
}

function findProjectedFile(itemId) {
  if (!snapshot) {
    return null;
  }

  for (const folder of [...snapshot.folders, snapshot.looseFiles]) {
    const file = folder.files.find((candidate) => candidate.id === itemId);
    if (file) {
      return createFileItem(file, folder.id);
    }
  }

  return null;
}

function findProjectedItem(itemId) {
  if (!snapshot) {
    return null;
  }

  const folder = snapshot.folders.find((candidate) => candidate.id === itemId);
  return folder ? createFolderItem(folder) : findProjectedFile(itemId);
}

function openItemMenu(item, clientX, clientY) {
  if (!item) {
    return;
  }

  contextItem = item;
  contextItemScreenPoint = toPhysicalScreenPoint(clientX, clientY);
  itemMenu.hidden = false;
  itemMenu.style.left = "0";
  itemMenu.style.top = "0";
  const position = placeFloatingPanel(
    clientX,
    clientY,
    itemMenu.offsetWidth,
    itemMenu.offsetHeight,
    globalThis.innerWidth,
    globalThis.innerHeight,
  );
  itemMenu.style.left = `${position.left}px`;
  itemMenu.style.top = `${position.top}px`;
  itemMenu.querySelector("button")?.focus();
  rerouteLastCursor();
}

function closeItemMenu() {
  if (itemMenu.hidden) {
    return;
  }

  itemMenu.hidden = true;
  contextItem = null;
  contextItemScreenPoint = null;
  rerouteLastCursor();
}

async function handleItemMenuCommand(action) {
  const item = contextItem;
  const screenPoint = contextItemScreenPoint;
  closeItemMenu();
  if (!item) {
    return;
  }

  if (action === "rename") {
    openRenameDialog(item);
  } else if (action === "recycle") {
    openRecycleDialog(item);
  } else if (action === "open" || action === "showInExplorer") {
    await executeImmediateItemCommand(action, item);
  } else if (action === "windowsOptions") {
    await executeWindowsOptions(item, screenPoint);
  }
}

function toPhysicalScreenPoint(clientX, clientY) {
  const scale = Number.isFinite(globalThis.devicePixelRatio)
    && globalThis.devicePixelRatio > 0
    ? globalThis.devicePixelRatio
    : 1;
  return {
    x: Math.round((widgetPhysicalRect?.x ?? 0) + clientX * scale),
    y: Math.round((widgetPhysicalRect?.y ?? 0) + clientY * scale),
  };
}

async function executeWindowsOptions(item, screenPoint) {
  if (!screenPoint) {
    setCommandIssue(
      "Windows 추가 옵션 메뉴를 열지 못했습니다.",
      "메뉴를 표시할 화면 위치를 확인할 수 없습니다.",
      null,
    );
    return;
  }

  let requestId = null;
  try {
    announceCommand(`${item.name} Windows 추가 옵션 준비`);
    const prepared = await sendShellMenuPrepare(item.id, screenPoint);
    if (!prepared.accepted || !prepared.ticket) {
      setCommandIssue(
        "Windows 추가 옵션 메뉴를 준비하지 못했습니다.",
        prepared.message,
        () => void executeWindowsOptions(item, screenPoint),
      );
      return;
    }

    requestId = prepared.requestId;
    activeShellMenus.set(requestId, { item, screenPoint });
    setIssue("command", null);
    await invoke(SeelenCommand.RequestFocus, { hwnd: widget.windowId });
    const companionPath = await resolveCompanionPath();
    await invoke(SeelenCommand.Run, {
      program: companionPath,
      args: ["--shell-menu-ticket", prepared.ticket],
      workingDir: companionPath.slice(0, companionPath.lastIndexOf("\\")),
      elevated: false,
    });
  } catch (error) {
    if (requestId) {
      activeShellMenus.delete(requestId);
      sendAuthenticated({ requestId, type: "cancelShellMenu" });
    }
    setCommandIssue(
      "Windows 추가 옵션 메뉴를 열지 못했습니다.",
      error instanceof Error ? error.message : String(error),
      () => void executeWindowsOptions(item, screenPoint),
    );
  }
}

function sendShellMenuPrepare(itemId, screenPoint) {
  const requestId = crypto.randomUUID();
  const message = {
    itemId,
    ownerWindow: widget.windowId,
    requestId,
    screenX: screenPoint.x,
    screenY: screenPoint.y,
    type: "prepareShellMenu",
  };

  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pendingCommands.delete(requestId);
      reject(new Error("Windows 추가 옵션 메뉴 준비 시간이 초과되었습니다."));
    }, 10_000);
    pendingCommands.set(requestId, { reject, resolve, timeout });
    if (!sendAuthenticated(message)) {
      clearTimeout(timeout);
      pendingCommands.delete(requestId);
      reject(new Error("Companion에 연결되어 있지 않습니다."));
    }
  });
}

function handleShellMenuCompletion(message) {
  const pending = activeShellMenus.get(message.requestId);
  if (!pending) {
    return;
  }

  activeShellMenus.delete(message.requestId);
  if (!message.succeeded) {
    setCommandIssue(
      "Windows 추가 옵션 메뉴를 완료하지 못했습니다.",
      message.message,
      () => void executeWindowsOptions(pending.item, pending.screenPoint),
    );
    return;
  }

  setIssue("command", null);
  announceCommand(
    message.commandInvoked
      ? "Windows Shell 명령을 완료했습니다."
      : "Windows 추가 옵션 메뉴를 닫았습니다.",
  );
  rerouteLastCursor();
}

async function executeImmediateItemCommand(action, item) {
  try {
    announceCommand(action === "open" ? `${item.name} 열기` : `${item.name} 위치 열기`);
    const result = await sendItemCommand(action, item.id);
    if (result.accepted) {
      showAcceptedCommandWarning(result);
      if (!result.message) {
        setIssue("command", null);
      }
      return;
    }

    const title = action === "open"
      ? "항목을 열지 못했습니다."
      : "탐색기에서 위치를 열지 못했습니다.";
    setCommandIssue(
      title,
      result.message,
      () => void executeImmediateItemCommand(action, item),
    );
  } catch (error) {
    setCommandIssue(
      "Companion 명령을 완료하지 못했습니다.",
      error instanceof Error ? error.message : String(error),
      () => void executeImmediateItemCommand(action, item),
    );
  }
}

function openRenameDialog(item) {
  closeRecycleDialog();
  closeMoveDialog({ refresh: false });
  renameTarget = item;
  document.getElementById("rename-description").textContent =
    `${item.kind === "folder" ? "폴더" : "파일"} “${item.name}”의 새 이름을 입력하세요.`;
  renameInput.value = item.name;
  renameError.textContent = "";
  renameDialog.hidden = false;
  setDialogBusy(renameDialog, false);
  validateRenameInput();
  renameInput.focus();
  renameInput.select();
  rerouteLastCursor();
}

function closeRenameDialog() {
  if (renameDialog.dataset.busy === "true") {
    return;
  }

  renameDialog.hidden = true;
  renameTarget = null;
  renameError.textContent = "";
  flushDeferredSnapshot();
  rerouteLastCursor();
}

function validateRenameInput() {
  const validation = validateWindowsName(renameInput.value);
  const unchanged = renameTarget && renameInput.value === renameTarget.name;
  renameError.textContent = validation ?? (unchanged ? "현재 이름과 동일합니다." : "");
  document.getElementById("rename-confirm").disabled = Boolean(validation || unchanged);
  return !validation && !unchanged;
}

async function submitRename() {
  if (!renameTarget || !validateRenameInput()) {
    return;
  }

  setDialogBusy(renameDialog, true);
  renameError.textContent = "";
  try {
    const result = await sendItemCommand(
      "rename",
      renameTarget.id,
      { newName: renameInput.value },
    );
    if (result.accepted) {
      setDialogBusy(renameDialog, false);
      closeRenameDialog();
      showAcceptedCommandWarning(result);
      announceCommand("이름을 변경했습니다.");
      return;
    }

    renameError.textContent = result.message || "이름을 변경하지 못했습니다.";
  } catch (error) {
    renameError.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    if (!renameDialog.hidden) {
      setDialogBusy(renameDialog, false);
      validateRenameInput();
    }
  }
}

function openRecycleDialog(item) {
  closeRenameDialog();
  closeMoveDialog({ refresh: false });
  recycleTarget = item;
  const nestedWarning = item.kind === "folder"
    ? "\n화면에 표시되지 않는 하위 폴더와 파일도 함께 이동합니다."
    : "";
  document.getElementById("recycle-description").textContent =
    `“${item.name}”을 Windows 휴지통으로 이동하시겠습니까?${nestedWarning}`;
  recycleError.textContent = "";
  recycleDialog.hidden = false;
  setDialogBusy(recycleDialog, false);
  document.getElementById("recycle-confirm").focus();
  rerouteLastCursor();
}

function closeRecycleDialog() {
  if (recycleDialog.dataset.busy === "true") {
    return;
  }

  recycleDialog.hidden = true;
  recycleTarget = null;
  recycleError.textContent = "";
  flushDeferredSnapshot();
  rerouteLastCursor();
}

async function submitRecycle() {
  if (!recycleTarget) {
    return;
  }

  setDialogBusy(recycleDialog, true);
  recycleError.textContent = "";
  try {
    const result = await sendItemCommand("recycle", recycleTarget.id);
    if (result.accepted) {
      setDialogBusy(recycleDialog, false);
      closeRecycleDialog();
      showAcceptedCommandWarning(result);
      announceCommand("휴지통으로 이동했습니다.");
      return;
    }

    recycleError.textContent = result.message || "휴지통으로 이동하지 못했습니다.";
  } catch (error) {
    recycleError.textContent = error instanceof Error ? error.message : String(error);
  } finally {
    if (!recycleDialog.hidden) {
      setDialogBusy(recycleDialog, false);
    }
  }
}

async function prepareFileMove(file, destination, desiredName = null) {
  try {
    announceCommand(`${destination.isLooseFiles ? "루트" : destination.name}(으)로 이동 준비`);
    const result = await sendItemCommand(
      "prepareMove",
      file.id,
      {
        destinationId: destination.id,
        newName: desiredName,
      },
    );
    if (!result.accepted) {
      setCommandIssue(
        "파일을 이동할 수 없습니다.",
        result.message,
        () => void prepareFileMove(file, destination, desiredName),
      );
      return;
    }

    const move = {
      destination,
      file,
      proposedName: result.proposedName || file.name,
    };
    if (result.hasNameCollision) {
      openMoveDialog(move);
    } else {
      await executePreparedMove(move, move.proposedName);
    }
  } catch (error) {
    setCommandIssue(
      "파일 이동을 준비하지 못했습니다.",
      error instanceof Error ? error.message : String(error),
      () => void prepareFileMove(file, destination, desiredName),
    );
  }
}

async function executePreparedMove(move, newName) {
  try {
    const result = await sendItemCommand(
      "move",
      move.file.id,
      {
        destinationId: move.destination.id,
        newName,
      },
    );
    if (result.accepted) {
      showAcceptedCommandWarning(result);
      announceCommand(`${newName}(으)로 이동했습니다.`);
      return true;
    }

    if (result.code === "NameCollision") {
      await refreshMoveProposal(move, newName, result.message);
      return false;
    }

    setCommandIssue(
      "파일을 이동하지 못했습니다.",
      result.message,
      () => void prepareFileMove(move.file, move.destination, newName),
    );
    return false;
  } catch (error) {
    setCommandIssue(
      "파일 이동 명령을 완료하지 못했습니다.",
      error instanceof Error ? error.message : String(error),
      () => void prepareFileMove(move.file, move.destination, newName),
    );
    return false;
  }
}

function openMoveDialog(move, errorMessage = "") {
  closeRenameDialog();
  closeRecycleDialog();
  pendingMove = move;
  document.getElementById("move-description").textContent =
    `“${move.file.name}”을 ${move.destination.isLooseFiles ? "루트" : `“${move.destination.name}”`}에 이동합니다. 덮어쓰지 않을 새 이름을 확인하세요.`;
  moveInput.value = move.proposedName;
  moveError.textContent = errorMessage;
  moveDialog.hidden = false;
  setDialogBusy(moveDialog, false);
  validateMoveInput();
  moveInput.focus();
  moveInput.select();
  rerouteLastCursor();
}

function closeMoveDialog({ refresh = false } = {}) {
  if (moveDialog.dataset.busy === "true") {
    return;
  }

  const wasOpen = !moveDialog.hidden;
  moveDialog.hidden = true;
  pendingMove = null;
  moveError.textContent = "";
  if (refresh && wasOpen) {
    retryRefresh();
  }
  flushDeferredSnapshot();
  rerouteLastCursor();
}

function cancelMoveDialog() {
  closeMoveDialog({ refresh: true });
  announceCommand("파일 이동을 취소했습니다.");
}

function validateMoveInput() {
  const validation = validateWindowsName(moveInput.value);
  moveError.textContent = validation ?? moveError.textContent;
  if (!validation && isNameValidationMessage(moveError.textContent)) {
    moveError.textContent = "";
  }
  document.getElementById("move-confirm").disabled = Boolean(validation);
  return !validation;
}

async function submitMove() {
  if (!pendingMove || !validateMoveInput()) {
    return;
  }

  const move = pendingMove;
  const newName = moveInput.value;
  setDialogBusy(moveDialog, true);
  moveError.textContent = "";
  const accepted = await executePreparedMove(move, newName);
  if (accepted) {
    setDialogBusy(moveDialog, false);
    closeMoveDialog({ refresh: false });
    return;
  }

  if (!moveDialog.hidden) {
    setDialogBusy(moveDialog, false);
    validateMoveInput();
  }
}

async function refreshMoveProposal(move, desiredName, message) {
  try {
    const prepared = await sendItemCommand(
      "prepareMove",
      move.file.id,
      {
        destinationId: move.destination.id,
        newName: desiredName,
      },
    );
    if (!prepared.accepted) {
      moveError.textContent = prepared.message || message || "이동 대상을 다시 확인해 주세요.";
      return;
    }

    const nextMove = {
      ...move,
      proposedName: prepared.proposedName || desiredName,
    };
    const nextMessage =
      `${message || "같은 이름이 새로 생겼습니다."} 새 이름을 다시 제안했습니다.`;
    if (moveDialog.hidden) {
      openMoveDialog(nextMove, nextMessage);
    } else {
      pendingMove = nextMove;
      moveInput.value = nextMove.proposedName;
      moveError.textContent = nextMessage;
    }
  } catch (error) {
    moveError.textContent = error instanceof Error ? error.message : String(error);
  }
}

function setDialogBusy(dialog, busy) {
  dialog.dataset.busy = String(busy);
  for (const control of dialog.querySelectorAll("button, input")) {
    control.disabled = busy;
  }
}

function sendItemCommand(
  action,
  itemId,
  {
    destinationId = null,
    newName = null,
  } = {},
) {
  const requestId = crypto.randomUUID();
  const message = {
    action,
    itemId,
    requestId,
    type: "itemCommand",
  };
  if (destinationId !== null) {
    message.destinationId = destinationId;
  }
  if (newName !== null) {
    message.newName = newName;
  }

  return new Promise((resolve, reject) => {
    const timeout = setTimeout(() => {
      pendingCommands.delete(requestId);
      reject(new Error("Companion 명령 응답 시간이 초과되었습니다."));
    }, 15_000);
    pendingCommands.set(requestId, { reject, resolve, timeout });
    if (!sendAuthenticated(message)) {
      clearTimeout(timeout);
      pendingCommands.delete(requestId);
      reject(new Error("Companion에 연결되어 있지 않습니다."));
    }
  });
}

function settlePendingCommand(message) {
  const pending = pendingCommands.get(message.requestId);
  if (!pending) {
    return;
  }

  clearTimeout(pending.timeout);
  pendingCommands.delete(message.requestId);
  pending.resolve(message);
}

function rejectPendingCommands(message) {
  for (const pending of pendingCommands.values()) {
    clearTimeout(pending.timeout);
    pending.reject(new Error(message));
  }
  pendingCommands.clear();
}

function setCommandIssue(title, message, retry) {
  setIssue(
    "command",
    createIssue(
      60,
      title,
      message || "파일이 외부에서 변경되었거나 Windows 명령을 완료하지 못했습니다.",
      retry,
    ),
  );
}

function showAcceptedCommandWarning(result) {
  if (!result.message) {
    setIssue("command", null);
    return;
  }

  setCommandIssue("파일 작업 후 확인이 필요합니다.", result.message, retryRefresh);
}

function announceCommand(message) {
  commandStatus.textContent = "";
  queueMicrotask(() => {
    commandStatus.textContent = message;
  });
}

function getFileDisplayName(file) {
  if (
    !file.extension
    || !file.name.toLowerCase().endsWith(file.extension.toLowerCase())
  ) {
    return file.name;
  }

  const nameWithoutExtension = file.name.slice(0, -file.extension.length);
  return nameWithoutExtension || file.name;
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
    return true;
  }
  return false;
}

function updateWatch(watch) {
  if (!watch) {
    return;
  }

  if (watch.warning) {
    setIssue(
      "watcher",
      createIssue(
        70,
        "폴더 변경 감시가 제한되었습니다.",
        watch.warning,
        retryRefresh,
      ),
    );
  } else if (!watch.contentWatching && !watch.parentWatching) {
    setIssue(
      "watcher",
      createIssue(
        70,
        "폴더 변경을 자동으로 감시할 수 없습니다.",
        "변경 내용이 자동으로 반영되지 않을 수 있습니다.",
        retryRefresh,
      ),
    );
  } else {
    setIssue("watcher", null);
  }
}

function closeSocket() {
  rejectPendingCommands("Companion 연결이 종료되었습니다.");
  sessionToken = null;
  httpBaseUrl = null;
  if (socket) {
    socket.close();
    socket = null;
  }
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
