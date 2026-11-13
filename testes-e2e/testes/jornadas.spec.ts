import { expect, test, type APIRequestContext, type Page } from '@playwright/test'

async function tokenDemo(request: APIRequestContext, perfil: string) {
  const r = await request.post('/api/auth/demo', { data: { perfil } })
  expect(r.ok()).toBeTruthy()
  return (await r.json()).accessToken as string
}

async function entrarDemo(page: Page, perfil: string) {
  await page.goto('/demo')
  await page.click(`[data-perfil="${perfil}"]`)
  await page.waitForURL((u) => !u.pathname.startsWith('/demo') || perfil === 'admin')
  await page.goto('/perfil')
  await expect(page.getByRole('button', { name: 'Sair' })).toBeVisible()
}

async function modoCentral(request: APIRequestContext, central: string, modo: string) {
  const t = await tokenDemo(request, 'admin')
  const r = await request.put(`/simulador/centrais/${central}/modo`, { data: { modo }, headers: { Authorization: `Bearer ${t}` } })
  expect(r.ok()).toBeTruthy()
}

async function pedirSocorro(page: Page) {
  await page.goto('/')
  await page.getByRole('button', { name: /Preciso de socorro/ }).first().click()
  await expect(page).toHaveURL(/\/socorro\//)
  return page.url().split('/socorro/')[1]
}

test('alerta proativo → pergunta "Você e sua família estão bem?" → Estou seguro', async ({ page, request }) => {
  const admin = await tokenDemo(request, 'admin')
  const area = '{"type":"Polygon","coordinates":[[[-34.97,-8.15],[-34.92,-8.15],[-34.92,-8.10],[-34.97,-8.10],[-34.97,-8.15]]]}'
  const r = await request.post('/api/demo/avisos', { data: { titulo: `Chuva intensa E2E ${Date.now()}`, severidade: 'Perigo', poligonoGeoJson: area, duracaoMinutos: 30 }, headers: { Authorization: `Bearer ${admin}` } })
  expect(r.status()).toBe(201)

  await entrarDemo(page, 'cidadao')
  await page.goto('/')
  const pergunta = page.getByRole('alertdialog')
  await expect(pergunta).toBeVisible({ timeout: 30_000 })
  await expect(pergunta).toContainText('Você e sua família estão bem?')
  await expect(pergunta).toContainText('DEMONSTRA')
  await pergunta.getByRole('button', { name: 'Estou seguro' }).click()
  await expect(page).toHaveURL(/\/seguro/)
  await page.getByRole('button', { name: 'Confirmar que estou seguro' }).click()
  await expect(page.getByRole('heading', { name: 'Situação registrada' })).toBeVisible()
  await expect(page.getByText('Não certifica a segurança')).toBeVisible()
})

test('pedido de socorro → IA/regras → centrais simuladas → painel → atualização pela central', async ({ page, browser }) => {
  const id = await pedirSocorro(page)
  await page.getByRole('button', { name: 'Estou ilhado' }).click()
  await page.getByRole('button', { name: 'Água subindo' }).click()
  await page.getByLabel('Pessoas no local').fill('3')
  await page.getByLabel('Descreva em poucas palavras').fill('Estamos na laje com uma criança de 5 anos')
  await page.getByRole('button', { name: 'Enviar informações' }).click()

  await expect(page.getByRole('heading', { name: 'Pedido recebido pelo servidor' })).toBeVisible()
  await expect(page.getByText(/Protocolo demonstrativo SIM-DC-RECIFE-/).first()).toBeVisible()
  await expect(page.getByText(/Protocolo demonstrativo SIM-CBM-SIM-/).first()).toBeVisible()
  await expect(page.getByText('Centrais simuladas: nenhum órgão real recebe')).toBeVisible()

  // Operador da Defesa Civil de Recife (simulada)
  const op = await (await browser.newContext()).newPage()
  await entrarDemo(op, 'defesa-civil-recife')
  await op.goto('/painel')
  await expect(op.getByText('AMBIENTE SIMULADO: NENHUM ÓRGÃO REAL FOI ACIONADO', { exact: false })).toBeVisible()
  await op.locator('[data-testid=ocorrencia-painel]').first().click()
  await expect(op.getByText(`Ocorrência ${id.slice(0, 8)}`)).toBeVisible()
  await expect(op.getByText('Ilhamento com água subindo').first()).toBeVisible()
  await expect(op.getByText('Recebimento confirmado').first()).toBeVisible()

  // Central receptora: muda o estado → retorno HTTP → cidadão vê
  await op.goto('/central/DC-RECIFE')
  await expect(op.getByText('CENTRAL SIMULADA — AMBIENTE DE DEMONSTRAÇÃO')).toBeVisible()
  await op.getByRole('button', { name: 'Equipe designada (fictícia)' }).first().click()
  await expect(op.getByText('retorno entregue ao Alerta em Ação')).toBeVisible()
})

test('offline: pedido fica salvo no aparelho e sincroniza sem duplicar ao voltar a conexão', async ({ page, context, request }) => {
  await page.goto('/')
  await expect(page.getByText('Conectado').or(page.getByRole('link', { name: 'Início' })).first()).toBeVisible()
  await context.setOffline(true)
  await page.getByRole('button', { name: /Preciso de socorro/ }).first().click()
  await expect(page.getByRole('heading', { name: 'Pedido salvo neste aparelho. Ainda não transmitido.' })).toBeVisible()
  const id = page.url().split('/socorro/')[1]

  await page.locator('a[href="/pedidos"]:visible').first().click()
  const item = page.locator('[data-testid=item-fila]').first()
  await expect(item).toHaveAttribute('data-estado', 'salvo_no_aparelho')
  await expect(item).toContainText('Ainda não transmitido')

  await context.setOffline(false)
  await page.evaluate(() => window.dispatchEvent(new Event('online')))
  await expect(item).toHaveAttribute('data-estado', 'recebido_servidor', { timeout: 30_000 })

  // Servidor recebeu exatamente uma vez.
  const disp = await page.evaluate(async () => {
    const db = await new Promise<IDBDatabase>((res) => { const r = indexedDB.open('alerta-em-acao'); r.onsuccess = () => res(r.result) })
    return await new Promise<string>((res) => { const g = db.transaction('config').objectStore('config').get('dispositivo'); g.onsuccess = () => res(g.result.valor) })
  })
  const lista = await (await request.get('/api/ocorrencias/do-dispositivo', { headers: { 'X-Dispositivo-Id': disp } })).json()
  expect(lista.filter((o: { id: string }) => o.id === id)).toHaveLength(1)
})

test('falha institucional: central indisponível, falha registrada e retentativa após reativação', async ({ page, request }) => {
  await page.context().setGeolocation({ latitude: -8.0145, longitude: -34.8556, accuracy: 20 }) // Olinda
  await modoCentral(request, 'DC-OLINDA', 'Indisponivel')
  try {
    await pedirSocorro(page)
    await expect(page.getByText('Falha; nova tentativa agendada').first()).toBeVisible({ timeout: 30_000 })
    await expect(page.getByText(/HTTP 503/).first()).toBeVisible()
  } finally {
    await modoCentral(request, 'DC-OLINDA', 'Normal')
  }
  await expect(page.getByText(/Protocolo demonstrativo SIM-DC-OLINDA-/).first()).toBeVisible({ timeout: 45_000 })
})

test('apoio comunitário: oferta → participação → organização simulada confirma', async ({ page, request }) => {
  await entrarDemo(page, 'voluntario')
  await page.goto('/ajudar')
  const descricao = `Água E2E ${Date.now()}`
  await page.getByLabel('O que você oferece').fill(descricao)
  await page.getByRole('button', { name: 'Registrar oferta' }).click()
  await expect(page.getByText('Oferta registrada')).toBeVisible()
  await page.getByRole('button', { name: `Participar com: ${descricao.slice(0, 28)}` }).first().click()
  await expect(page.getByText('Recebida pela organização').first()).toBeVisible({ timeout: 30_000 })

  const org = await tokenDemo(request, 'organizacao')
  const painel = await (await request.get('/api/painel/organizacao/ofertas', { headers: { Authorization: `Bearer ${org}` } })).json()
  const oferta = painel.ofertas.find((o: { descricao: string }) => o.descricao === descricao)
  const r = await request.patch(`/simulador/centrais/ORG-MAOS/recebimentos/${oferta.protocoloOrganizacao}/estado`, { data: { estado: 'Aceita', nota: 'Obrigado' }, headers: { Authorization: `Bearer ${org}` } })
  expect(r.ok()).toBeTruthy()
  await expect(page.getByText('Participação confirmada').first()).toBeVisible({ timeout: 20_000 })
})

test('mapa interativo e abrigos com disponibilidade não verificada', async ({ page }) => {
  await page.goto('/mapa')
  await expect(page.getByRole('region', { name: /Mapa interativo/ })).toBeVisible()
  await page.getByRole('radio', { name: 'Estações de chuva' }).click()
  await expect(page.getByRole('radio', { name: 'Estações de chuva' })).toHaveAttribute('aria-checked', 'true')
  await expect(page.locator('text=Abrigo ativo mais próximo >> visible=true').first()).toBeVisible()
  // Experiência pública: nenhum abrigo fictício; disponibilidade nunca é inventada.
  const publico = await (await page.request.get('/api/abrigos')).json()
  expect(publico.abrigos.some((a: { ficticio: boolean }) => a.ficticio)).toBeFalsy()
  await page.goto('/abrigos')
  await expect(page.getByText(/Nenhum abrigo ativo publicado|Disponibilidade não confirmada/).first()).toBeVisible()
})
