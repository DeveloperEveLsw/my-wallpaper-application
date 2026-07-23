import assert from "node:assert/strict";
import test from "node:test";
import {
  isNameValidationMessage,
  isValidFileMoveDestination,
  placeFloatingPanel,
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

test("Glass 메뉴를 작업 영역 가장자리 안에 배치한다", () => {
  assert.deepEqual(
    placeFloatingPanel(1900, 1000, 230, 180, 1920, 1080),
    { left: 1680, top: 890 },
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
