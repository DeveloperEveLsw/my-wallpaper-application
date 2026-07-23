import { invoke, SeelenCommand, Widget } from "@seelen-ui/lib";

const EXPECTED_ORIGIN = "http://tauri.localhost";
const PORT_START = 43127;
const PORT_COUNT = 9;
const PROTOCOL_VERSION = 1;
const MAX_BLOB_BYTES = 1024 * 1024;
const PNG_SIGNATURE = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];

const widget = Widget.self;
const root = document.getElementById("m0-desktop");
const elements = {
  status: document.getElementById("connection-status"),
  origin: document.getElementById("origin-value"),
  host: document.getElementById("host-value"),
  desktopRoot: document.getElementById("desktop-root"),
  reconnectCount: document.getElementById("reconnect-count"),
  clickCount: document.getElementById("click-count"),
  log: document.getElementById("test-log"),
  icon: document.getElementById("icon-preview"),
  image: document.getElementById("image-preview"),
};

let socket = null;
let sessionToken = null;
let reconnectCount = 0;
let clickCount = 0;
let connectionGeneration = 0;
let reconnectTimer = null;
let pingTimer = null;
let objectUrls = [];

await widget.init({ autoSizeByContent: root });
bindInputTests();
elements.origin.textContent = window.location.origin;
await widget.ready();
void reconnect("초기 연결");

function bindInputTests() {
  document.getElementById("click-test").addEventListener("click", () => {
    clickCount += 1;
    elements.clickCount.textContent = String(clickCount);
    setLog(`좌클릭 ${clickCount}회 수신.`);
  });

  document.getElementById("drag-handle").addEventListener("pointerdown", async (event) => {
    if (event.button !== 0) {
      return;
    }

    setLog("네이티브 Desktop 위젯 drag 시작.");
    await widget.window.startDragging();
  });

  document.getElementById("popup-test").addEventListener("click", async () => {
    await invoke(SeelenCommand.TriggerWidget, {
      payload: { id: "@wallpaper/m0-popup" },
    });
    setLog("Popup trigger 전송. 입력란의 실제 포커스를 확인하세요.");
  });

  document.getElementById("reconnect-test").addEventListener("click", () => {
    void reconnect("수동 재연결");
  });
}

async function reconnect(reason) {
  const generation = ++connectionGeneration;
  clearTimeout(reconnectTimer);
  clearInterval(pingTimer);
  reconnectTimer = null;
  pingTimer = null;
  closeSocket();
  clearBlobUrls();

  reconnectCount += 1;
  elements.reconnectCount.textContent = String(reconnectCount);
  setStatus("pending", `${reason}: nonce 생성`);

  try {
    if (window.location.origin !== EXPECTED_ORIGIN) {
      throw new Error(
        `Origin 불일치: 실제 ${window.location.origin}, 허용 ${EXPECTED_ORIGIN}`,
      );
    }

    const nonceBytes = crypto.getRandomValues(new Uint8Array(32));
    const nonce = encodeBase64Url(nonceBytes);
    const nonceId = encodeBase64Url(
      new Uint8Array(await crypto.subtle.digest("SHA-256", nonceBytes)),
    );

    const companionPath = await resolveCompanionPath();
    try {
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
    } catch {
      throw new Error("Seelen Run 권한이 거부되었거나 Companion 실행에 실패했습니다.");
    }

    setStatus("pending", "Companion 증명 대기");
    const port = await discoverCompanionPort(nonceBytes, nonceId, generation);
    if (generation !== connectionGeneration) {
      return;
    }

    const connected = await openAuthenticatedSocket(port, nonce, generation);
    if (generation !== connectionGeneration) {
      connected.socket.close();
      return;
    }

    socket = connected.socket;
    sessionToken = connected.hello.sessionToken;
    elements.host.textContent = `127.0.0.1:${port}`;
    elements.desktopRoot.textContent = connected.hello.desktopRoot;
    elements.desktopRoot.title = connected.hello.desktopRoot;

    await validateBlobResponses(connected.hello.httpBaseUrl, sessionToken, generation);
    if (generation !== connectionGeneration) {
      return;
    }

    setStatus("ok", "hello + Blob 검증 통과");
    setLog("세션 토큰은 현재 위젯 메모리에만 유지됩니다.");
    adoptSocketLifecycle(socket, generation);
  } catch (error) {
    if (generation !== connectionGeneration) {
      return;
    }

    const message = error instanceof Error ? error.message : String(error);
    setStatus("error", "연결 실패");
    setLog(message);

    if (!message.includes("Run") && !message.includes("Origin 불일치")) {
      reconnectTimer = setTimeout(() => void reconnect("자동 재연결"), 2500);
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

  return `${localAppData}\\WallpaperSeelenM0\\Wallpaper.Seelen.M0.Companion.exe`;
}

async function discoverCompanionPort(nonceBytes, nonceId, generation) {
  const deadline = performance.now() + 12_000;
  const hmacKey = await crypto.subtle.importKey(
    "raw",
    nonceBytes,
    { hash: "SHA-256", name: "HMAC" },
    false,
    ["sign"],
  );
  while (performance.now() < deadline && generation === connectionGeneration) {
    for (let offset = 0; offset < PORT_COUNT; offset += 1) {
      const port = PORT_START + offset;
      const challenge = crypto.getRandomValues(new Uint8Array(32));
      const encodedChallenge = encodeBase64Url(challenge);

      try {
        const response = await fetch(
          `http://127.0.0.1:${port}/bootstrap-proof?nonceId=${encodeURIComponent(
            nonceId,
          )}&challenge=${encodeURIComponent(encodedChallenge)}`,
          {
            cache: "no-store",
            method: "GET",
            signal: AbortSignal.timeout(500),
          },
        );
        if (!response.ok) {
          continue;
        }

        const body = await response.json();
        if (body.protocol !== PROTOCOL_VERSION || typeof body.proof !== "string") {
          continue;
        }

        const expectedProof = new Uint8Array(
          await crypto.subtle.sign(
            "HMAC",
            hmacKey,
            challenge,
          ),
        );
        const actualProof = decodeBase64Url(body.proof);
        if (fixedTimeEqual(expectedProof, actualProof)) {
          return port;
        }
      } catch {
        // An unused or occupied non-Companion port is expected during discovery.
      }
    }

    await delay(200);
  }

  throw new Error("인증 가능한 loopback Companion 포트를 찾지 못했습니다.");
}

function openAuthenticatedSocket(port, nonce, generation) {
  return new Promise((resolve, reject) => {
    const candidate = new WebSocket(`ws://127.0.0.1:${port}/ws`);
    let settled = false;
    const timeout = setTimeout(() => {
      fail(new Error("WebSocket hello 시간이 초과되었습니다."));
    }, 5000);

    const fail = (error) => {
      if (settled) {
        return;
      }

      settled = true;
      clearTimeout(timeout);
      candidate.close();
      reject(error);
    };

    candidate.addEventListener(
      "open",
      () => {
        if (generation !== connectionGeneration) {
          fail(new Error("대체된 연결입니다."));
          return;
        }

        candidate.send(
          JSON.stringify({
            type: "hello",
            protocol: PROTOCOL_VERSION,
            nonce,
          }),
        );
      },
      { once: true },
    );

    candidate.addEventListener(
      "message",
      (event) => {
        if (settled) {
          return;
        }

        try {
          const hello = JSON.parse(event.data);
          validateHello(hello, port);
          settled = true;
          clearTimeout(timeout);
          resolve({ hello, socket: candidate });
        } catch (error) {
          fail(error);
        }
      },
      { once: true },
    );

    candidate.addEventListener(
      "error",
      () => fail(new Error("WebSocket 연결이 거부되었습니다.")),
      { once: true },
    );
    candidate.addEventListener(
      "close",
      () => fail(new Error("hello 전에 WebSocket이 종료되었습니다.")),
      { once: true },
    );
  });
}

function validateHello(hello, port) {
  if (
    hello?.type !== "helloAck" ||
    hello.protocol !== PROTOCOL_VERSION ||
    decodeBase64Url(hello.sessionToken).length !== 32 ||
    hello.httpBaseUrl !== `http://127.0.0.1:${port}` ||
    typeof hello.desktopRoot !== "string" ||
    !/^[A-Za-z]:\\/.test(hello.desktopRoot)
  ) {
    throw new Error("Companion helloAck 형식이 올바르지 않습니다.");
  }
}

async function validateBlobResponses(httpBaseUrl, token, generation) {
  const pairs = [
    ["icon", elements.icon],
    ["image", elements.image],
  ];

  for (const [kind, image] of pairs) {
    const expectedUrl = `${httpBaseUrl}/blob/${kind}`;
    const response = await fetch(expectedUrl, {
      cache: "no-store",
      headers: { Authorization: `Bearer ${token}` },
    });

    const contentLength = Number(response.headers.get("Content-Length"));
    if (
      generation !== connectionGeneration ||
      response.url !== expectedUrl ||
      response.status !== 200 ||
      response.headers.get("Content-Type") !== "image/png" ||
      !Number.isSafeInteger(contentLength) ||
      contentLength < PNG_SIGNATURE.length ||
      contentLength > MAX_BLOB_BYTES
    ) {
      throw new Error(`${kind} HTTP 응답 메타데이터 검증 실패.`);
    }

    const bytes = new Uint8Array(await response.arrayBuffer());
    if (
      bytes.length !== contentLength ||
      !PNG_SIGNATURE.every((value, index) => bytes[index] === value)
    ) {
      throw new Error(`${kind} Blob 본문 검증 실패.`);
    }

    const blobUrl = URL.createObjectURL(new Blob([bytes], { type: "image/png" }));
    objectUrls.push(blobUrl);
    image.src = blobUrl;
    await image.decode();
  }
}

function adoptSocketLifecycle(activeSocket, generation) {
  activeSocket.addEventListener("close", () => {
    if (generation === connectionGeneration) {
      sessionToken = null;
      setStatus("pending", "WebSocket 종료, 재연결 대기");
      reconnectTimer = setTimeout(() => void reconnect("연결 복구"), 1000);
    }
  });

  pingTimer = setInterval(() => {
    if (
      generation === connectionGeneration &&
      activeSocket.readyState === WebSocket.OPEN &&
      sessionToken
    ) {
      activeSocket.send(
        JSON.stringify({
          type: "ping",
          sessionToken,
          timestamp: Date.now(),
        }),
      );
    }
  }, 5000);
}

function closeSocket() {
  sessionToken = null;
  if (socket) {
    socket.onclose = null;
    socket.close();
    socket = null;
  }
}

function clearBlobUrls() {
  for (const url of objectUrls) {
    URL.revokeObjectURL(url);
  }

  objectUrls = [];
  elements.icon.removeAttribute("src");
  elements.image.removeAttribute("src");
}

function encodeBase64Url(bytes) {
  let binary = "";
  for (const value of bytes) {
    binary += String.fromCharCode(value);
  }

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
  if (left.length !== right.length) {
    return false;
  }

  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left[index] ^ right[index];
  }

  return difference === 0;
}

function setStatus(state, text) {
  elements.status.dataset.state = state;
  elements.status.textContent = text;
}

function setLog(text) {
  elements.log.textContent = text;
}

function delay(milliseconds) {
  return new Promise((resolve) => setTimeout(resolve, milliseconds));
}
