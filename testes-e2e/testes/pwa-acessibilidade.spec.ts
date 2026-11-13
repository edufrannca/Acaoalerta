import { expect, test } from '@playwright/test'
import AxeBuilder from '@axe-core/playwright'

test('PWA instalável: manifest, ícones maskable, standalone, service worker e rotas internas offline', async ({ page, context }) => {
  await page.goto('/')
  const href = await page.locator('link[rel="manifest"]').getAttribute('href')
  const m = await (await page.request.get(href!)).json()
  expect(m.name).toBe('Alerta em Ação — RMR')
  expect(m.display).toBe('standalone')
  expect(m.lang).toBe('pt-BR')
  expect(m.start_url).toBe('/')
  expect(m.icons.some((i: { purpose?: string }) => i.purpose === 'maskable')).toBeTruthy()
  for (const i of m.icons) expect((await page.request.get(i.src)).ok()).toBeTruthy()
  await expect(page.locator('meta[name="apple-mobile-web-app-capable"]')).toHaveAttribute('content', 'yes')
  await page.waitForFunction(async () => !!(await navigator.serviceWorker.getRegistration())?.active, null, { timeout: 15000 })
  await page.reload()
  await context.setOffline(true)
  for (const rota of ['/pedidos', '/mapa', '/instalar']) {
    await page.goto(rota)
    await expect(page.locator('main')).toBeVisible()
  }
  await expect(page.getByText('Você está sem internet').first()).toBeVisible()
  await context.setOffline(false)
})

test('página de instalação mostra QR Code, instruções Android/iPhone e diagnóstico', async ({ page }) => {
  await page.goto('/instalar')
  await expect(page.getByRole('img', { name: /QR Code para/ })).toBeVisible()
  await expect(page.getByText('Android (Chrome)')).toBeVisible()
  await expect(page.getByText('iPhone (Safari)')).toBeVisible()
  await expect(page.getByText('Ambiente de demonstração.')).toBeVisible()
  await expect(page.getByText('Service worker')).toBeVisible()
})

for (const rota of ['/', '/instalar', '/pedidos', '/ajudar', '/perfil']) {
  test(`acessibilidade automática (axe, WCAG 2 A/AA) em ${rota}`, async ({ page }) => {
    await page.goto(rota)
    await page.waitForLoadState('networkidle')
    const r = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).exclude('.maplibregl-canvas').analyze()
    const graves = r.violations.filter((v) => v.impact === 'critical' || v.impact === 'serious')
    expect(graves.map((v) => `${v.id}: ${v.nodes.length} elemento(s) — ${v.nodes[0]?.target}`)).toEqual([])
  })
}

test('alto contraste e texto ampliado persistem após recarregar', async ({ page }) => {
  await page.goto('/')
  await page.getByRole('button', { name: /Acessibilidade/ }).click()
  await page.getByRole('button', { name: 'Aumentar texto' }).click()
  await page.getByRole('checkbox', { name: /Alto contraste/ }).check()
  await page.reload()
  await expect(page.locator('html')).toHaveAttribute('data-contraste', 'alto')
  expect(await page.evaluate(() => document.documentElement.style.getPropertyValue('zoom'))).toBe('1.15')
})
