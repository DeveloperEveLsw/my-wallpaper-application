import * as THREE from 'three';

export const baselineSceneDescriptor = Object.freeze({
  id: 'baseline',
  title: 'Baseline',
  description: 'WebView2와 three.js 렌더 경로를 검증하는 기본 장면입니다.',
  palette: 'neutral',
  create: () => new BaselineScene(),
});

class BaselineScene {
  constructor() {
    this.scene = new THREE.Scene();
    this.scene.background = new THREE.Color(0x05070b);

    this.camera = new THREE.PerspectiveCamera(42, 16 / 9, 0.1, 40);
    this.camera.position.set(0, 2.4, 6.5);
    this.camera.lookAt(0, 0.4, 0);

    this.scene.add(new THREE.HemisphereLight(0xffffff, 0x1b2638, 2.4));

    const keyLight = new THREE.DirectionalLight(0xdceaff, 4.8);
    keyLight.position.set(-3, 6, 5);
    keyLight.castShadow = true;
    this.scene.add(keyLight);

    this.cubeGeometry = new THREE.BoxGeometry(2, 2, 2);
    this.cubeMaterial = new THREE.MeshStandardMaterial({
      color: 0xb8cbe6,
      roughness: 0.28,
      metalness: 0.12,
    });
    this.cube = new THREE.Mesh(this.cubeGeometry, this.cubeMaterial);
    this.cube.position.y = 0.55;
    this.cube.castShadow = true;
    this.scene.add(this.cube);

    this.floorGeometry = new THREE.PlaneGeometry(18, 18);
    this.floorMaterial = new THREE.MeshStandardMaterial({
      color: 0x0b111b,
      roughness: 0.82,
    });
    this.floor = new THREE.Mesh(this.floorGeometry, this.floorMaterial);
    this.floor.rotation.x = -Math.PI / 2;
    this.floor.position.y = -0.55;
    this.floor.receiveShadow = true;
    this.scene.add(this.floor);

    this.elapsedSeconds = 0;
  }

  update(deltaSeconds) {
    this.elapsedSeconds += deltaSeconds;
    this.cube.rotation.x = this.elapsedSeconds * 0.24;
    this.cube.rotation.y = this.elapsedSeconds * 0.38;
    this.cube.position.y = 0.55 + Math.sin(this.elapsedSeconds * 0.8) * 0.12;
  }

  resize(width, height) {
    this.camera.aspect = Math.max(1, width) / Math.max(1, height);
    this.camera.updateProjectionMatrix();
  }

  dispose() {
    this.scene.remove(this.cube, this.floor);
    this.cubeGeometry.dispose();
    this.cubeMaterial.dispose();
    this.floorGeometry.dispose();
    this.floorMaterial.dispose();
  }
}
