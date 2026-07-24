const NAME_VALIDATION_MESSAGES = new Set([
  "이름을 입력해 주세요.",
  "점(.)만으로 된 상대 경로 이름은 사용할 수 없습니다.",
  "이름 끝에는 공백이나 마침표를 사용할 수 없습니다.",
  "Windows 이름에 사용할 수 없는 문자가 포함되어 있습니다.",
  "이름은 255자를 넘을 수 없습니다.",
  "Windows 예약 장치 이름은 사용할 수 없습니다.",
]);

export function validateWindowsName(name) {
  if (!name || name.trim().length === 0) {
    return "이름을 입력해 주세요.";
  }
  if (name === "." || name === "..") {
    return "점(.)만으로 된 상대 경로 이름은 사용할 수 없습니다.";
  }
  if (name.endsWith(" ") || name.endsWith(".")) {
    return "이름 끝에는 공백이나 마침표를 사용할 수 없습니다.";
  }
  if (/[\u0000-\u001f<>:"/\\|?*]/u.test(name)) {
    return "Windows 이름에 사용할 수 없는 문자가 포함되어 있습니다.";
  }
  if (name.length > 255) {
    return "이름은 255자를 넘을 수 없습니다.";
  }

  const deviceName = name.split(".", 1)[0].replace(/[ .]+$/u, "").toUpperCase();
  const reserved = /^(?:CON|PRN|AUX|NUL|CLOCK\$|CONIN\$|CONOUT\$|COM[1-9¹²³]|LPT[1-9¹²³])$/u;
  if (reserved.test(deviceName)) {
    return "Windows 예약 장치 이름은 사용할 수 없습니다.";
  }
  return null;
}

export function isNameValidationMessage(message) {
  return NAME_VALIDATION_MESSAGES.has(message);
}

export function isValidFileMoveDestination(sourceFolderId, destinationId) {
  return typeof sourceFolderId === "string"
    && sourceFolderId.length > 0
    && typeof destinationId === "string"
    && destinationId.length > 0
    && sourceFolderId !== destinationId;
}

export function hasExceededDragThreshold(
  startX,
  startY,
  currentX,
  currentY,
  minimumDistance = 5,
) {
  const values = [startX, startY, currentX, currentY, minimumDistance];
  if (!values.every(Number.isFinite) || minimumDistance < 0) {
    return false;
  }

  return Math.hypot(currentX - startX, currentY - startY) >= minimumDistance;
}

export function reorderIds(orderedIds, sourceId, targetId, insertAfter) {
  if (!Array.isArray(orderedIds)) {
    return [];
  }

  const result = [...orderedIds];
  const sourceIndex = result.indexOf(sourceId);
  const initialTargetIndex = result.indexOf(targetId);
  if (
    sourceIndex < 0
    || initialTargetIndex < 0
    || sourceIndex === initialTargetIndex
  ) {
    return result;
  }

  const [moved] = result.splice(sourceIndex, 1);
  const targetIndex = result.indexOf(targetId);
  result.splice(targetIndex + (insertAfter ? 1 : 0), 0, moved);
  return result;
}

export function placeFloatingPanel(
  clientX,
  clientY,
  panelWidth,
  panelHeight,
  viewportWidth,
  viewportHeight,
  margin = 10,
) {
  const maxLeft = Math.max(margin, viewportWidth - panelWidth - margin);
  const maxTop = Math.max(margin, viewportHeight - panelHeight - margin);
  const preferredTop = clientY + panelHeight <= viewportHeight - margin
    ? clientY
    : clientY - panelHeight;

  return {
    left: Math.min(Math.max(margin, clientX), maxLeft),
    top: Math.min(Math.max(margin, preferredTop), maxTop),
  };
}
