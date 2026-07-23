import { Widget } from "@seelen-ui/lib";

const widget = Widget.self;
const root = document.getElementById("m0-popup");
const input = document.getElementById("keyboard-input");
const result = document.getElementById("keyboard-result");

await widget.init({ autoSizeByContent: root });

widget.onTrigger(() => {
  input.value = "";
  result.textContent = "Popup 포커스 수신, 입력 대기 중";
  setTimeout(() => input.focus(), 0);
});

input.addEventListener("keydown", (event) => {
  if (event.key === "Enter") {
    result.textContent = input.value
      ? `Enter 수신: ${input.value}`
      : "Enter 수신: 빈 문자열";
  }
});

document.addEventListener("keydown", (event) => {
  if (event.key === "Escape") {
    void widget.hide();
  }
});

document.getElementById("close-popup").addEventListener("click", () => {
  void widget.hide();
});

await widget.ready({ show: false });
