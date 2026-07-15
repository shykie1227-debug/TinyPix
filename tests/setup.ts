import '@testing-library/jest-dom';

const createStorage = () => {
  const store = new Map<string, string>();
  return {
    getItem: (key: string) => store.get(key) ?? null,
    setItem: (key: string, value: string) => {
      store.set(key, value);
    },
    removeItem: (key: string) => {
      store.delete(key);
    },
    clear: () => {
      store.clear();
    },
    key: (index: number) => Array.from(store.keys())[index] ?? null,
    get length() {
      return store.size;
    },
  };
};

if (!globalThis.localStorage) {
  Object.defineProperty(globalThis, 'localStorage', {
    value: createStorage(),
    configurable: true,
  });
}

// Radix UI 依赖 ResizeObserver（jsdom 未提供）
if (!globalThis.ResizeObserver) {
  class ResizeObserverMock {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  Object.defineProperty(globalThis, 'ResizeObserver', {
    value: ResizeObserverMock,
    configurable: true,
  });
}

// Radix UI 依赖 DOMRect（jsdom 部分场景下未提供）
if (typeof globalThis.DOMRect === 'undefined') {
  class DOMRectMock {
    constructor(
      public x = 0,
      public y = 0,
      public width = 0,
      public height = 0
    ) {}
    static fromRect(other?: { x?: number; y?: number; width?: number; height?: number }) {
      return new DOMRectMock(other?.x, other?.y, other?.width, other?.height);
    }
    get top() { return this.y; }
    get left() { return this.x; }
    get right() { return this.x + this.width; }
    get bottom() { return this.y + this.height; }
    toJSON() { return JSON.stringify(this); }
  }
  Object.defineProperty(globalThis, 'DOMRect', {
    value: DOMRectMock,
    configurable: true,
  });
}

if (typeof globalThis.HTMLCanvasElement !== 'undefined') {
  Object.defineProperty(globalThis.HTMLCanvasElement.prototype, 'getContext', {
    configurable: true,
    value: function getContext(this: HTMLCanvasElement) {
      return {
        canvas: this,
        filter: 'none',
        globalAlpha: 1,
        save() {},
        restore() {},
        clearRect() {},
        translate() {},
        rotate() {},
        scale() {},
        drawImage() {},
        getImageData: (_x: number, _y: number, width: number, height: number) => ({
          data: new Uint8ClampedArray(width * height * 4),
          width,
          height,
        }),
        putImageData() {},
      };
    },
  });
}
