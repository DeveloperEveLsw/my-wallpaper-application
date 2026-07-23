import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const [html, script] = await Promise.all([
  readFile(new URL("../desktop/index.html", import.meta.url), "utf8"),
  readFile(new URL("../src/desktop.js", import.meta.url), "utf8"),
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

test("M3 Glass 메뉴와 세 확인 대화상자 계약을 제공한다", () => {
  for (const action of ["open", "showInExplorer", "rename", "recycle"]) {
    assert.match(html, new RegExp(`data-item-command="${action}"`, "u"));
  }
  for (const formId of ["rename-form", "recycle-form", "move-form"]) {
    assert.match(html, new RegExp(`id="${formId}"`, "u"));
  }
});

test("위젯은 프로토콜 4 itemCommand만 사용한다", () => {
  assert.match(script, /const PROTOCOL_VERSION = 4;/u);
  assert.match(script, /type: "itemCommand"/u);
  assert.doesNotMatch(script, /type: "openFile"/u);
});
