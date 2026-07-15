import { createRequire } from 'node:module';
import { execFileSync, spawn } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, readdirSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const requireFromBuild = createRequire(join(process.cwd(), 'package.json'));
const { chromium } = requireFromBuild('playwright-core');

const exePath = process.env.TINYPIX_EXE || join(process.env.USERPROFILE, 'Desktop', 'tiny', 'TinyPix-Pro-3.5.1-Windows-x64-Portable.exe');
const engineDir = process.env.TINYPIX_ENGINE_DIR || join(process.env.LOCALAPPDATA, 'TinyPixBuild', 'src-tauri', 'resources');
const sampleDir = process.env.TINYPIX_SAMPLE_DIR || join(process.env.TEMP, 'TinyPixMediaMatrix');
const artifactDir = process.env.TINYPIX_E2E_ARTIFACTS || 'C:\\Mac\\Home\\TinyPix\\3.5pro\\artifacts\\windows\\app-e2e';
const selectFileScript = process.env.TINYPIX_SELECT_FILE_SCRIPT || 'C:\\Mac\\Home\\TinyPix\\3.5pro\\scripts\\windows_select_file.ps1';
const cdpPort = Number(process.env.TINYPIX_CDP_PORT || 9223);
const sampleVideo = join(sampleDir, 'output.webm');
const sampleImage = join(sampleDir, 'app-source.png');
const videoOutput = join(sampleDir, 'output_output.mp4');
const gifOutput = join(sampleDir, 'output.gif');
const editOutput = join(sampleDir, 'output_edited.mp4');
const imageOutputDir = join(sampleDir, 'tinypix_output');

const sleep = (milliseconds) => new Promise((resolve) => setTimeout(resolve, milliseconds));
const assert = (condition, message) => {
  if (!condition) throw new Error(message);
};

mkdirSync(artifactDir, { recursive: true });
assert(existsSync(exePath), `EXE not found: ${exePath}`);
assert(existsSync(sampleVideo), `Video sample not found: ${sampleVideo}`);

execFileSync(join(engineDir, 'ffmpeg.exe'), [
  '-hide_banner', '-loglevel', 'error', '-y',
  '-f', 'lavfi', '-i', 'testsrc2=size=640x360:rate=1', '-frames:v', '1', sampleImage,
], { stdio: 'inherit' });
for (const path of [videoOutput, gifOutput, editOutput]) rmSync(path, { force: true });
rmSync(imageOutputDir, { recursive: true, force: true });

const app = spawn(exePath, [], {
  env: {
    ...process.env,
    WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${cdpPort}`,
  },
  stdio: 'ignore',
});

let browser;
let activePage;
const result = {
  generatedAt: new Date().toISOString(),
  exePath,
  sampleVideo,
  sampleImage,
  checks: [],
  screenshots: [],
  consoleErrors: [],
};

const check = (name, details = {}) => result.checks.push({ name, passed: true, ...details });

async function waitForCdp() {
  for (let attempt = 0; attempt < 80; attempt += 1) {
    try {
      const response = await fetch(`http://127.0.0.1:${cdpPort}/json/version`);
      if (response.ok) return;
    } catch {}
    await sleep(250);
  }
  throw new Error('WebView2 CDP endpoint did not become ready');
}

async function dropLocalFile(page, path) {
  await page.locator('.tinypix-drop-zone').evaluate((element, localPath) => {
    const file = new File([new Uint8Array([0])], localPath, { type: 'application/octet-stream' });
    Object.defineProperty(file, 'path', { value: localPath });
    const dataTransfer = new DataTransfer();
    dataTransfer.items.add(file);
    element.dispatchEvent(new DragEvent('drop', { bubbles: true, cancelable: true, dataTransfer }));
  }, path);
}

async function screenshot(page, name) {
  const path = join(artifactDir, name);
  await page.screenshot({ path });
  result.screenshots.push(path);
}

try {
  await waitForCdp();
  browser = await chromium.connectOverCDP(`http://127.0.0.1:${cdpPort}`);
  const context = browser.contexts()[0];
  const pages = context.pages();
  const page = pages.find((candidate) => candidate.url().startsWith('http://tauri.localhost')) || pages[0];
  activePage = page;
  await page.waitForLoadState('domcontentloaded');
  await page.waitForTimeout(1000);
  await page.evaluate(() => localStorage.removeItem('tinypix-options'));
  await page.reload({ waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(500);
  await page.evaluate(() => {
    const bridge = window.__TAURI_INTERNALS__;
    if (!bridge || bridge.__tinypixE2EWrapped) return;
    const originalInvoke = bridge.invoke.bind(bridge);
    window.__TINYPIX_E2E_IPC__ = [];
    bridge.invoke = async (command, args, options) => {
      try {
        const value = await originalInvoke(command, args, options);
        window.__TINYPIX_E2E_IPC__.push({ command, args, value });
        return value;
      } catch (error) {
        window.__TINYPIX_E2E_IPC__.push({ command, args, error: String(error) });
        throw error;
      }
    };
    bridge.__tinypixE2EWrapped = true;
  });
  page.on('pageerror', (error) => result.consoleErrors.push(`pageerror: ${error.message}`));
  page.on('console', (message) => {
    if (message.type() === 'error') result.consoleErrors.push(`console: ${message.text()}`);
  });
  await page.getByText('引擎状态: 就绪', { exact: true }).waitFor({ timeout: 60000 });
  check('embedded-engine-ready');

  await dropLocalFile(page, sampleVideo);
  await page.getByText('output.webm', { exact: true }).first().waitFor({ timeout: 15000 });
  const video = page.locator('video').first();
  await video.waitFor({ state: 'attached', timeout: 30000 });
  await page.waitForFunction(() => {
    const node = document.querySelector('video');
    return Boolean(node?.getAttribute('src')?.includes('.mp4'));
  }, undefined, { timeout: 60000 });
  const previewSrc = await video.getAttribute('src');
  assert(previewSrc?.includes('.mp4'), 'Unsupported WebM did not receive an MP4 preview proxy');
  check('video-proxy-preview', { previewSrc });
  await screenshot(page, 'video-output-loaded.png');

  await page.getByRole('button', { name: '开始输出', exact: true }).click();
  await page.getByText('全部完成，共 1 个', { exact: true }).waitFor({ timeout: 90000 });
  const videoIpc = await page.evaluate(() => window.__TINYPIX_E2E_IPC__);
  result.ipcLog = videoIpc;
  const videoCall = [...videoIpc].reverse().find((entry) => entry.command === 'convert_video_format');
  const actualVideoOutput = videoCall?.value?.output_path || videoCall?.value?.outputPath || videoOutput;
  assert(existsSync(actualVideoOutput), `Video output missing: ${actualVideoOutput}`);
  check('video-output-through-exe', { outputPath: actualVideoOutput });

  await page.getByRole('button', { name: 'GIF 制作', exact: true }).click();
  await page.getByRole('button', { name: '开始转换', exact: true }).waitFor({ timeout: 15000 });
  await page.waitForFunction(() => {
    const input = document.querySelector('#gif-end-time');
    return input instanceof HTMLInputElement && input.value !== '' && input.value !== '00:00.00';
  }, undefined, { timeout: 30000 });
  await screenshot(page, 'gif-loaded.png');
  await page.getByRole('button', { name: '开始转换', exact: true }).click();
  await page.getByText('全部完成，共 1 个', { exact: true }).waitFor({ timeout: 90000 });
  assert(existsSync(gifOutput), `GIF output missing: ${gifOutput}`);
  check('gif-through-exe', { outputPath: gifOutput });

  await page.getByRole('button', { name: '视频剪辑', exact: true }).click();
  const precise = page.getByRole('checkbox', { name: /精确边界/ });
  await precise.waitFor({ timeout: 30000 });
  await precise.check();
  const exportButton = page.getByRole('button', { name: '合并导出', exact: true });
  await exportButton.waitFor({ state: 'visible', timeout: 30000 });
  await page.waitForFunction(() => {
    const button = [...document.querySelectorAll('button')].find((item) => item.textContent?.includes('合并导出'));
    return button && !button.disabled;
  }, undefined, { timeout: 30000 });
  await screenshot(page, 'video-editor-loaded.png');
  await exportButton.click();
  await page.getByText(/已导出：/).waitFor({ timeout: 90000 });
  assert(existsSync(editOutput), `Edited video output missing: ${editOutput}`);
  check('precise-video-edit-through-exe', { outputPath: editOutput });

  await page.getByRole('button', { name: '图片工具', exact: true }).click();
  await dropLocalFile(page, sampleImage);
  await page.getByText('app-source.png', { exact: true }).first().waitFor({ timeout: 15000 });
  const canvas = page.locator('canvas').first();
  await canvas.waitFor({ timeout: 30000 });
  await page.waitForFunction(() => {
    const node = document.querySelector('canvas');
    return node instanceof HTMLCanvasElement && node.width > 0 && node.height > 0 && node.toDataURL().length > 1000;
  }, undefined, { timeout: 30000 });
  const beforeRotate = await canvas.evaluate((node) => node.toDataURL());
  await page.getByRole('button', { name: '右旋', exact: true }).click();
  await page.waitForFunction((before) => {
    const node = document.querySelector('canvas');
    return node instanceof HTMLCanvasElement && node.toDataURL() !== before;
  }, beforeRotate, { timeout: 10000 });
  check('image-canvas-preview-and-rotation');
  await screenshot(page, 'image-preview-rotated.png');
  await page.getByRole('button', { name: '开始处理', exact: true }).click();
  await page.getByText('已完成', { exact: true }).waitFor({ timeout: 90000 });
  const imageOutputs = existsSync(imageOutputDir)
    ? readdirSync(imageOutputDir).filter((name) => name.endsWith('.webp'))
    : [];
  assert(imageOutputs.length > 0, `Image output missing in: ${imageOutputDir}`);
  const imageOutput = join(imageOutputDir, imageOutputs[0]);
  const signature = readFileSync(imageOutput).subarray(0, 12).toString('ascii');
  assert(signature.startsWith('RIFF') && signature.endsWith('WEBP'), 'Image output does not have a WebP signature');
  check('image-preview-and-export-through-exe', { outputPath: imageOutput });

  result.passed = result.consoleErrors.length === 0;
  assert(result.passed, `WebView reported console errors: ${result.consoleErrors.join(' | ')}`);
} catch (error) {
  result.passed = false;
  result.error = error instanceof Error ? error.stack : String(error);
  if (activePage) {
    try {
      const failureScreenshot = join(artifactDir, 'failure.png');
      await activePage.screenshot({ path: failureScreenshot });
      result.screenshots.push(failureScreenshot);
      result.failureBodyText = await activePage.locator('body').innerText();
    } catch {}
  }
  throw error;
} finally {
  writeFileSync(join(artifactDir, 'report.json'), `${JSON.stringify(result, null, 2)}\n`, 'utf8');
  if (browser) await browser.close().catch(() => {});
  try { execFileSync('taskkill.exe', ['/PID', String(app.pid), '/T', '/F'], { stdio: 'ignore' }); } catch {}
}

console.log(`TinyPix Windows app E2E passed: ${result.checks.length} checks`);
