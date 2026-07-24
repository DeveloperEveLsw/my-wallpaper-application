import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [html, script, metadata, styles] = await Promise.all([
  readFile(new URL("../desktop/index.html", import.meta.url), "utf8"),
  readFile(new URL("../src/desktop.js", import.meta.url), "utf8"),
  readFile(new URL("../desktop/metadata.yml", import.meta.url), "utf8"),
  readFile(new URL("../desktop/index.css", import.meta.url), "utf8"),
]);

test("스크립트가 조회하는 정적 DOM id를 HTML이 모두 제공한다", () => {
  const requestedIds = [
    ...script.matchAll(/document\.getElementById\("([^"]+)"\)/gu),
  ].map((match) => match[1]);
  const missing = [...new Set(requestedIds)].filter(
    (id) => !html.includes(`id="${id}"`),
  );

  assert.deepEqual(missing, []);
});

test("M3·M5 Glass 메뉴와 세 확인 대화상자 계약을 제공한다", () => {
  for (const action of [
    "open",
    "showInExplorer",
    "rename",
    "recycle",
    "windowsOptions",
  ]) {
    assert.match(html, new RegExp(`data-item-command="${action}"`, "u"));
  }
  for (const formId of ["rename-form", "recycle-form", "move-form"]) {
    assert.match(html, new RegExp(`id="${formId}"`, "u"));
  }
});

test("Seelen GUI에서 우측 상단 오류 메시지를 끄고 켤 수 있다", () => {
  assert.match(metadata, /key: showErrorMessages/u);
  assert.match(metadata, /ko: 오류 메시지 표시/u);
  assert.match(
    script,
    /showErrorMessages = config\.showErrorMessages !== false;/u,
  );
  assert.match(script, /!activeIssue \|\| !showErrorMessages/u);
});

test("데스크톱 위젯에 상단 시계와 날짜를 표시하지 않는다", () => {
  assert.doesNotMatch(html, /desktop-clock|clock-time|clock-date/u);
  assert.doesNotMatch(script, /TIME_FORMATTER|DATE_FORMATTER|updateClock/u);
  assert.doesNotMatch(styles, /desktop-clock|clock-time|clock-date/u);
});

test("위젯은 프로토콜 5의 itemCommand와 Shell Broker 준비 명령을 사용한다", () => {
  assert.match(script, /const PROTOCOL_VERSION = 5;/u);
  assert.match(script, /type: "itemCommand"/u);
  assert.match(script, /type: "prepareShellMenu"/u);
  assert.match(script, /SeelenCommand\.RequestFocus/u);
  assert.match(script, /"--shell-menu-ticket"/u);
  assert.match(script, /ownerWindow: widget\.windowId/u);
  assert.match(script, /screenX: screenPoint\.x/u);
  assert.match(script, /screenY: screenPoint\.y/u);
  assert.doesNotMatch(script, /type: "openFile"/u);
});

test("Seelen WebView와 호환되는 포인터 드래그만 사용한다", () => {
  assert.match(script, /addEventListener\("pointerdown"/u);
  assert.match(script, /setPointerCapture/u);
  assert.match(script, /addEventListener\(\s*"lostpointercapture"/u);
  assert.doesNotMatch(script, /\.draggable\s*=/u);
  assert.doesNotMatch(
    script,
    /addEventListener\("(?:(?:drag(?:start|enter|over|leave|end))|drop)"/u,
  );
  assert.doesNotMatch(script, /dataTransfer/u);
});
