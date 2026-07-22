export class SceneRegistry {
  constructor() {
    this.descriptors = new Map();
  }

  register(descriptor) {
    if (!descriptor?.id || typeof descriptor.create !== 'function') {
      throw new TypeError('A visualizer scene needs an id and create function.');
    }

    if (this.descriptors.has(descriptor.id)) {
      throw new Error(`Visualizer scene is already registered: ${descriptor.id}`);
    }

    this.descriptors.set(descriptor.id, Object.freeze({ ...descriptor }));
    return this;
  }

  list() {
    return [...this.descriptors.values()].map(({ create: _, ...metadata }) => metadata);
  }

  create(id, context) {
    const descriptor = this.descriptors.get(id);
    if (!descriptor) {
      throw new Error(`Unknown visualizer scene: ${id}`);
    }

    return descriptor.create(context);
  }
}
