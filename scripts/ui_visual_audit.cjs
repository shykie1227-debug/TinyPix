const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const { chromium } = require('playwright');

const root = path.resolve(__dirname, '..');
const output = path.join(root, 'artifacts', 'ui');
fs.rmSync(output, { recursive: true, force: true });
fs.mkdirSync(output, { recursive: true });

const configurations = [
  { name: '1200x800-100', physicalWidth: 1200, physicalHeight: 800, scale: 1 },
  { name: '900x600-100', physicalWidth: 900, physicalHeight: 600, scale: 1 },
  { name: '900x600-125', physicalWidth: 900, physicalHeight: 600, scale: 1.25 },
  { name: '900x600-150', physicalWidth: 900, physicalHeight: 600, scale: 1.5 },
];

const routes = [
  { name: 'video-output', open: async (page) => page.getByRole('button', { name: '视频工具', exact: true }).click() },
  { name: 'gif-maker', open: async (page) => page.getByRole('button', { name: 'GIF 制作', exact: true }).click() },
  { name: 'video-trimmer', open: async (page) => page.getByRole('button', { name: '视频剪辑', exact: true }).click() },
  { name: 'image-workbench', open: async (page) => page.getByRole('button', { name: '图片工具', exact: true }).click() },
];

async function inspectLayout(page) {
  return page.evaluate(() => {
    const root = document.documentElement;
    const clippedText = [...document.querySelectorAll('button, label, p, span, h1, h2, h3')]
      .filter((element) => {
        const rect = element.getBoundingClientRect();
        if (rect.width <= 2 || rect.height <= 2) return false;
        if (element.querySelector('input, select, textarea')) return false;
        const style = getComputedStyle(element);
        const horizontalClip = element.scrollWidth > element.clientWidth + 1;
        const verticalClip = element.scrollHeight > element.clientHeight + 1;
        const intentionallyScrollable = ['auto', 'scroll'].includes(style.overflowX) || ['auto', 'scroll'].includes(style.overflowY);
        return !intentionallyScrollable && (horizontalClip || verticalClip);
      })
      .slice(0, 20)
      .map((element) => ({
        text: (element.textContent || '').trim().slice(0, 80),
        client: [element.clientWidth, element.clientHeight],
        scroll: [element.scrollWidth, element.scrollHeight],
      }));
    return {
      viewport: [window.innerWidth, window.innerHeight],
      document: [root.scrollWidth, root.scrollHeight],
      horizontalOverflow: root.scrollWidth > root.clientWidth + 1,
      clippedText,
    };
  });
}

async function primaryActionIsVisible(page, routeName) {
  const name = routeName === 'image-workbench' ? '选择本地图片' : '选择本地视频';
  const button = page.getByRole('button', { name, exact: true });
  const box = await button.boundingBox();
  if (!box) return false;
  const viewport = await page.evaluate(() => ({ width: window.innerWidth, height: window.innerHeight }));
  return box.x >= 0 && box.y >= 0 && box.x + box.width <= viewport.width && box.y + box.height <= viewport.height - 48;
}

function resizeToPhysicalPixels(file, config) {
  if (config.scale === 1) return;
  execFileSync('sips', ['--resampleHeightWidth', String(config.physicalHeight), String(config.physicalWidth), file], { stdio: 'ignore' });
}

(async () => {
  const browser = await chromium.launch({
    headless: true,
    executablePath: '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
    args: ['--disable-gpu'],
  });
  const report = [];
  for (const config of configurations) {
    const context = await browser.newContext({
      viewport: {
        width: Math.round(config.physicalWidth / config.scale),
        height: Math.round(config.physicalHeight / config.scale),
      },
      deviceScaleFactor: 1,
      locale: 'zh-CN',
    });
    for (const route of routes) {
      const page = await context.newPage();
      const errors = [];
      page.on('pageerror', (error) => errors.push(`pageerror: ${error.message}`));
      page.on('console', (message) => {
        if (message.type() === 'error') errors.push(`console: ${message.text()}`);
      });
      await page.goto('http://127.0.0.1:5173/', { waitUntil: 'networkidle' });
      await route.open(page);
      await page.waitForTimeout(700);
      const screenshot = path.join(output, `${route.name}-${config.name}.png`);
      await page.screenshot({ path: screenshot, fullPage: false, animations: 'disabled' });
      resizeToPhysicalPixels(screenshot, config);
      report.push({ route: route.name, configuration: config.name, screenshot, primaryActionVisible: await primaryActionIsVisible(page, route.name), ...(await inspectLayout(page)), errors: [...errors] });
      await page.close();
    }
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', (error) => errors.push(`pageerror: ${error.message}`));
    page.on('console', (message) => {
      if (message.type() === 'error') errors.push(`console: ${message.text()}`);
    });
    await page.goto('http://127.0.0.1:5173/', { waitUntil: 'networkidle' });
    await page.getByRole('button', { name: '设置', exact: true }).click();
    await page.waitForTimeout(700);
    const settingsScreenshot = path.join(output, `settings-${config.name}.png`);
    await page.screenshot({ path: settingsScreenshot, fullPage: false });
    resizeToPhysicalPixels(settingsScreenshot, config);
    report.push({ route: 'settings', configuration: config.name, screenshot: settingsScreenshot, ...(await inspectLayout(page)), errors: [...errors] });
    await page.close();
    await context.close();
  }
  await browser.close();
  const reportPath = path.join(output, 'layout-report.json');
  fs.writeFileSync(reportPath, JSON.stringify(report, null, 2));
  const failures = report.filter((entry) => entry.horizontalOverflow || entry.clippedText.length || entry.errors.length || entry.primaryActionVisible === false);
  console.log(JSON.stringify({ reportPath, audited: report.length, failures }, null, 2));
  process.exitCode = failures.length ? 1 : 0;
})().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
