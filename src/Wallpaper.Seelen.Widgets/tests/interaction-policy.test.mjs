import assert from "node:assert/strict";
import test from "node:test";
import {
  hasExceededDragThreshold,
  isNameValidationMessage,
  isValidFileMoveDestination,
  placeFloatingPanel,
  reorderIds,
  validateWindowsName,
} from "../src/interaction-policy.js";

test("Windows 이름 규칙과 255자 경계를 검사한다", () => {
  assert.equal(validateWindowsName("report.txt"), null);
  assert.equal(validateWindowsName(".gitignore"), null);
  assert.equal(validateWindowsName("archive.tar.gz"), null);
  assert.equal(validateWindowsName("a".repeat(255)), null);
  assert.match(validateWindowsName(""), /입력/u);
  assert.match(validateWindowsName("CON.txt"), /예약/u);
  assert.match(validateWindowsName("COM¹.log"), /예약/u);
  assert.match(validateWindowsName("bad?.txt"), /사용할 수 없는 문자/u);
  assert.match(validateWindowsName("trailing."), /끝/u);
  assert.match(validateWindowsName("a".repeat(256)), /255/u);
  assert.equal(isNameValidationMessage(validateWindowsName("NUL")), true);
  assert.equal(isNameValidationMessage("외부 충돌이 발생했습니다."), false);
});

test("M4의 세 이동 경로와 같은 카드 금지를 구분한다", () => {
  assert.equal(
    isValidFileMoveDestination("virtual:loose-files", "folder:WORK"),
    true,
  );
  assert.equal(
    isValidFileMoveDestination("folder:WORK", "virtual:loose-files"),
    true,
  );
  assert.equal(
    isValidFileMoveDestination("folder:WORK", "folder:ARCHIVE"),
    true,
  );
  assert.equal(
    isValidFileMoveDestination("folder:WORK", "folder:WORK"),
    false,
  );
  assert.equal(isValidFileMoveDestination("", "folder:WORK"), false);
});

test("포인터 드래그는 5px 이동 뒤에만 시작한다", () => {
  assert.equal(hasExceededDragThreshold(10, 10, 13, 13), false);
  assert.equal(hasExceededDragThreshold(10, 10, 15, 10), true);
  assert.equal(hasExceededDragThreshold(10, 10, 6, 7), true);
  assert.equal(hasExceededDragThreshold(0, 0, Number.NaN, 10), false);
});

test("포인터 drop 위치에 따라 Dock ID 순서를 바꾼다", () => {
  assert.deepEqual(
    reorderIds(["A", "B", "C", "D"], "A", "C", false),
    ["B", "A", "C", "D"],
  );
  assert.deepEqual(
    reorderIds(["A", "B", "C", "D"], "A", "C", true),
    ["B", "C", "A", "D"],
  );
  assert.deepEqual(
    reorderIds(["A", "B"], "missing", "B", true),
    ["A", "B"],
  );
});

test("Glass 메뉴를 작업 영역 가장자리 안에 배치한다", () => {
  assert.deepEqual(
    placeFloatingPanel(1900, 1000, 230, 180, 1920, 1080),
    { left: 1680, top: 820 },
  );
  assert.deepEqual(
    placeFloatingPanel(-20, -30, 230, 180, 1920, 1080),
    { left: 10, top: 10 },
  );
  assert.deepEqual(
    placeFloatingPanel(40, 50, 230, 180, 1920, 1080),
    { left: 40, top: 50 },
  );
});

test("Glass 메뉴는 아래 공간이 부족하면 포인터 위로 펼친다", () => {
  assert.deepEqual(
    placeFloatingPanel(300, 500, 230, 180, 1280, 600),
    { left: 300, top: 320 },
  );
  assert.deepEqual(
    placeFloatingPanel(300, 400, 230, 180, 1280, 600),
    { left: 300, top: 400 },
  );
});
