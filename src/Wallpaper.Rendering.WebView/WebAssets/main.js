import * as THREE from 'three';
import { SceneRegistry } from './scene-registry.js';
import { baselineSceneDescriptor } from './scenes/baseline-scene.js';

const hostElement = document.querySelector('#visualizer');
const renderer = new THREE.WebGLRenderer({
  antialias: true,
  alpha: false,
  powerPreference: 'high-performance',
});
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
renderer.setSize(hostElement.clientWidth, hostElement.clientHeight, false);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 0.92;
renderer.shadowMap.enabled = true;
renderer.shadowMap.type = THREE.PCFSoftShadowMap;
hostElement.append(renderer.domElement);

const registry = new SceneRegistry().register(baselineSceneDescriptor);
let selectedSceneId = 'baseline';
let selectedScene = registry.create(selectedSceneId, { renderer });
let lifecycleState = 'stopped';
let animationFrame = 0;
let lastTimestamp = 0;

const postToHost = message => {
  window.chrome?.webview?.postMessage(message);
};

const resize = () => {
  const width = Math.max(1, hostElement.clientWidth);
  const height = Math.max(1, hostElement.clientHeight);
  renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));
  renderer.setSize(width, height, false);
  selectedScene.resize(width, height);
};

const render = timestampMilliseconds => {
  animationFrame = 0;
  if (lifecycleState !== 'running') {
    return;
  }

  const timestampSeconds = timestampMilliseconds / 1000;
  const deltaSeconds = lastTimestamp === 0
    ? 1 / 60
    : Math.min(0.1, Math.max(0, timestampSeconds - lastTimestamp));
  lastTimestamp = timestampSeconds;
  selectedScene.update(deltaSeconds, timestampSeconds);
  window.__wallpaperVisualizerStatus = {
    sceneId: selectedSceneId,
    lifecycleState,
  };

  renderer.render(selectedScene.scene, selectedScene.camera);
  animationFrame = window.requestAnimationFrame(render);
};

const startRendering = () => {
  if (animationFrame !== 0 || lifecycleState !== 'running') {
    return;
  }

  lastTimestamp = 0;
  animationFrame = window.requestAnimationFrame(render);
};

const stopRendering = () => {
  if (animationFrame !== 0) {
    window.cancelAnimationFrame(animationFrame);
    animationFrame = 0;
  }
};

const selectScene = async sceneId => {
  if (sceneId === selectedSceneId) {
    return;
  }

  const nextScene = registry.create(sceneId, { renderer });
  nextScene.resize(hostElement.clientWidth, hostElement.clientHeight);
  const previousScene = selectedScene;
  selectedScene = nextScene;
  selectedSceneId = sceneId;
  previousScene.dispose();
};

window.chrome?.webview?.addEventListener('message', event => {
  const message = event.data;
  if (!message || typeof message !== 'object') {
    return;
  }

  switch (message.type) {
    case 'lifecycle':
      lifecycleState = message.state;
      if (lifecycleState === 'running') {
        startRendering();
      } else {
        stopRendering();
      }
      break;
    case 'select-scene':
      selectScene(message.sceneId).catch(error => postToHost({
        type: 'error',
        message: error instanceof Error ? error.message : String(error),
      }));
      break;
    default:
      break;
  }
});

const resizeObserver = new ResizeObserver(resize);
resizeObserver.observe(hostElement);
window.addEventListener('beforeunload', () => {
  stopRendering();
  resizeObserver.disconnect();
  selectedScene.dispose();
  renderer.dispose();
});

resize();
postToHost({
  type: 'ready',
  scenes: registry.list(),
  selectedSceneId,
});

if (!window.chrome?.webview) {
  lifecycleState = 'running';
  startRendering();
}
