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
  await expect(page.getByText(/Sem internet\./).first()).toBeVisible()
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

for (const rota of ['/', '/mapa', '/alertas', '/abrigos', '/instalar', '/pedidos', '/ajudar', '/perfil', '/demo']) {
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

test('experiência pública sem usuários ou centrais simuladas no login; área de demonstração separada', async ({ page }) => {
  await page.goto('/perfil')
  await expect(page.getByRole('heading', { name: 'Entrar' })).toBeVisible()
  await expect(page.getByText('Recuperar acesso')).toBeVisible()
  await expect(page.locator('[data-perfil]')).toHaveCount(0)
  await expect(page.getByText(/Defesa Civil Recife \(simulada\)/)).toHaveCount(0)
  await page.goto('/demo')
  await expect(page.getByText(/ÁREA DE DEMONSTRAÇÃO/)).toBeVisible()
  await expect(page.locator('[data-perfil]').first()).toBeVisible()
})

test('layout mobile: três ações com socorro em destaque e navegação inferior com 5 itens', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 })
  await page.goto('/')
  const socorro = page.getByRole('button', { name: /Preciso de socorro/ })
  const seguro = page.getByRole('link', { name: /Estou seguro/ })
  const bS = (await socorro.boundingBox())!, bE = (await seguro.boundingBox())!
  expect(bS.width).toBeGreaterThan(bE.width)
  const nav = page.getByRole('navigation', { name: 'Navegação principal' }).last()
  for (const r of ['Início', 'Mapa', 'Alertas', 'Abrigos', 'Perfil']) await expect(nav.getByRole('link', { name: r })).toBeVisible()
})

test('acessibilidade (axe) no cadastro com autorizações, perfil e cadastro de local', async ({ page }) => {
  await page.goto('/perfil')
  await page.getByRole('button', { name: 'Criar uma conta' }).click()
  const analisar = async () => {
    const r = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa']).exclude('.maplibregl-canvas').analyze()
    expect(r.violations.map((v) => `${v.id}: ${v.nodes.length}`)).toEqual([])
  }
  await analisar()
  await page.getByLabel('Nome').fill('Pessoa Acessível')
  await page.getByLabel('E-mail').fill(`a11y-${crypto.randomUUID().slice(0, 8)}@teste.alerta.br`)
  await page.getByLabel('Senha').fill('senha-segura-123')
  await page.getByRole('button', { name: 'Criar conta' }).click()
  await expect(page.getByRole('heading', { name: 'Autorizações (LGPD)' })).toBeVisible()
  await analisar()
  await page.goto('/locais/novo')
  await expect(page.getByRole('heading', { name: 'Novo local monitorado' })).toBeVisible()
  await analisar()
})
