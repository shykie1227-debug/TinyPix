export const APP_DESIGN_WIDTH = 1080;
export const APP_DESIGN_HEIGHT = 640;
export const APP_MIN_ZOOM = 0.8;

const clamp = (value: number, minimum: number, maximum: number) =>
  Math.min(maximum, Math.max(minimum, value));

export const calculateAppZoom = (logicalWidth: number, logicalHeight: number) => {
  const widthRatio = logicalWidth / APP_DESIGN_WIDTH;
  const heightRatio = logicalHeight / APP_DESIGN_HEIGHT;
  return clamp(Math.min(widthRatio, heightRatio), APP_MIN_ZOOM, 1);
};

export const calculateInitialWindowSize = (workAreaWidth: number, workAreaHeight: number) => ({
  width: clamp(Math.round(workAreaWidth * 0.88), 900, APP_DESIGN_WIDTH),
  height: clamp(Math.round(workAreaHeight * 0.88), 520, APP_DESIGN_HEIGHT),
});
