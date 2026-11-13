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

  await expect(page.getByRole('heading', { name: 'Seu pedido de ajuda foi registrado!' })).toBeVisible()
  await expect(page.getByText('Agora estamos acompanhando o envio para a equipe responsável.')).toBeVisible()
  await expect(page.getByText('Isso não significa que o socorro já está a caminho.')).toBeVisible()
  await expect(page.getByText('A equipe de teste recebeu seu pedido.')).toBeVisible({ timeout: 20_000 })
  await expect(page.getByText(/equipes são simuladas e não representam atendimento real/)).toBeVisible()
  await expect(page.getByText(/servidor/)).toHaveCount(0)
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
  await expect(page.getByRole('heading', { name: 'Seu pedido está salvo neste celular. Ainda não foi enviado.' })).toBeVisible()
  const id = page.url().split('/socorro/')[1]

  await page.locator('a[href="/pedidos"]:visible').first().click()
  const item = page.locator('[data-testid=item-fila]').first()
  await expect(item).toHaveAttribute('data-estado', 'salvo_no_aparelho')
  await expect(item).toContainText('Ainda não foi enviado')

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

test('sem internet: pedido salvo → código QR → aparelho próximo encaminha → solicitante sincroniza sem duplicar', async ({ page, context, browser }) => {
  await page.goto('/')
  await page.waitForFunction(() => navigator.serviceWorker?.controller != null, null, { timeout: 15_000 }).catch(() => {})
  await context.setOffline(true)
  await page.getByRole('button', { name: /Preciso de socorro/ }).first().click()
  await expect(page).toHaveURL(/\/socorro\//)
  const id = page.url().split('/socorro/')[1]
  await expect(page.getByRole('heading', { name: 'Seu pedido está salvo neste celular. Ainda não foi enviado.' })).toBeVisible()
  await page.getByRole('button', { name: 'Água potável' }).click()
  await page.getByRole('button', { name: 'Estou ilhado' }).click()
  await page.getByRole('button', { name: 'Enviar informações' }).click()
  await page.getByLabel('Mostrar um resumo do meu pedido para a comunidade').check()
  await page.getByRole('button', { name: 'Procurar celulares próximos para enviar' }).click()
  const qr = page.getByRole('img', { name: 'Código QR com o pedido de socorro' })
  await expect(qr).toBeVisible()
  await expect(page.getByText('Código mostrado para um celular próximo')).toBeVisible()
  const url = await qr.getAttribute('data-url')
  expect(url).toContain('/retransmitir#p=')

  // Outro aparelho, com internet, abre o link lido pela câmera.
  const ajudante = await browser.newContext({ geolocation: { latitude: -8.1263, longitude: -34.9479, accuracy: 20 }, permissions: ['geolocation'] })
  const pa = await ajudante.newPage()
  await pa.goto(new URL(url!).pathname + new URL(url!).hash)
  await expect(pa.getByRole('heading', { name: 'Alguém por perto pediu socorro' })).toBeVisible()
  await expect(pa.getByText('Pessoas ilhadas')).toBeVisible()
  await expect(pa.getByText('Precisa de: Água potável')).toBeVisible()
  await pa.getByLabel(/Enviar também a posição aproximada deste aparelho/).check()
  await pa.getByRole('button', { name: 'Encaminhar pedido' }).click()
  await expect(pa.getByRole('heading', { name: /O pedido de ajuda foi registrado!|A equipe de teste recebeu o pedido/ })).toBeVisible({ timeout: 20_000 })
  await ajudante.close()

  // O solicitante volta a ter internet: o mesmo pedido sincroniza sem duplicar.
  await context.setOffline(false)
  await expect(page.getByRole('heading', { name: 'Seu pedido de ajuda foi registrado!' })).toBeVisible({ timeout: 30_000 })
  await expect(page.getByText(/Níveis 2–3 · lido por QR/)).toBeVisible({ timeout: 15_000 })
  expect(id).toMatch(/[0-9a-f-]{36}/)
})

test('comunidade vê resumo autorizado, sem dados pessoais, e oferece ajuda coordenada pela equipe', async ({ page, request, browser }) => {
  // Pedido de outra família, compartilhado com necessidades.
  const id = crypto.randomUUID()
  const disp = 'e2e-' + crypto.randomUUID()
  const r = await request.post('/api/ocorrencias', { headers: { 'X-Dispositivo-Id': disp }, data: {
    id, criadaEm: new Date().toISOString(), latitude: -8.1270, longitude: -34.9490, precisaoMetros: 15,
    complemento: { quantidadePessoas: 5, criancas: 2, idosos: 1, aguaSubindo: true, necessidades: ['agua', 'alimentos'], descricao: 'Joana, fone 81 97777-1234' },
    compartilhamento: { autorizado: true, incluirPessoas: true, incluirNecessidades: true },
  } })
  expect(r.ok()).toBeTruthy()
  const ref = id.replace(/-/g, '').slice(0, 6).toUpperCase()

  await page.goto('/ajudar')
  await page.getByRole('button', { name: 'Buscar perto de mim' }).click({ timeout: 3000 }).catch(() => {})
  const cartao = page.getByRole('article', { name: `Pedido ${ref}` })
  await expect(cartao).toBeVisible({ timeout: 20_000 })
  // Comunidade vê só o básico: necessidades, região aproximada, verificação e estado.
  await expect(cartao).toContainText('Água potável')
  await expect(cartao).toContainText('Não verificado')
  await expect(cartao).not.toContainText('pessoas no local')
  await expect(cartao).not.toContainText('Crianças')
  await expect(page.locator('body')).not.toContainText('Joana')
  await expect(page.locator('body')).not.toContainText('97777')
  await cartao.getByRole('button', { name: 'Oferecer ajuda' }).click()
  const form = page.getByRole('region', { name: `Oferecer ajuda ao pedido ${ref}` })
  await expect(form).toContainText('Ajuda comunitária não é resgate.')
  await form.getByLabel('O que você oferece').fill('Fardo de água mineral')
  await expect(form.getByRole('button', { name: 'Enviar oferta' })).toBeDisabled()
  await form.getByLabel('Li as orientações e não vou entrar em áreas perigosas').check()
  await form.getByRole('button', { name: 'Enviar oferta' }).click()
  await expect(page.getByText('Oferta enviada · aguardando a equipe')).toBeVisible({ timeout: 20_000 })

  // Equipe autorizada aceita e orienta.
  const tk = (await (await request.post('/api/auth/demo', { data: { perfil: 'defesa-civil-recife' } })).json()).accessToken as string
  const lista = await (await request.get('/api/painel/ofertas-comunitarias', { headers: { Authorization: `Bearer ${tk}` } })).json() as { id: string; ocorrenciaId: string }[]
  const oferta = lista.find((o) => o.ocorrenciaId === id)!
  expect((await request.post(`/api/painel/ofertas/${oferta.id}/decisao`, { data: { aceita: true }, headers: { Authorization: `Bearer ${tk}` } })).ok()).toBeTruthy()
  expect((await request.post(`/api/painel/ofertas/${oferta.id}/encaminhamento`, { data: { instrucao: 'Entregar na escola municipal, portão 2 (ponto seguro).' }, headers: { Authorization: `Bearer ${tk}` } })).ok()).toBeTruthy()
  await page.reload()
  await expect(page.getByText(/Orientação da equipe:.*escola municipal/)).toBeVisible({ timeout: 20_000 })
  await page.getByRole('button', { name: 'Informar entrega concluída' }).click()
  await expect(page.getByText('Concluída').first()).toBeVisible()
  void browser
})

test('familiar vinculado é avisado quando o morador pede socorro em casa', async ({ browser }) => {
  test.setTimeout(150_000)
  const sufixo = crypto.randomUUID().slice(0, 8)
  // Aparelho B — morador: cria conta autorizando notificar familiares.
  const ctxB = await browser.newContext({ geolocation: { latitude: -8.0631, longitude: -34.8711, accuracy: 15 }, permissions: ['geolocation'] })
  const b = await ctxB.newPage()
  await b.goto('/perfil')
  await b.getByRole('button', { name: 'Criar uma conta' }).click()
  await b.getByLabel('Nome').fill('Maria Moradora')
  await b.getByLabel('E-mail').fill(`maria-${sufixo}@teste.alerta.br`)
  await b.getByLabel('Senha').fill('senha-segura-123')
  // Todas as autorizações começam desmarcadas.
  for (const cb of await b.getByRole('checkbox').all()) await expect(cb).not.toBeChecked()
  await b.getByLabel(/Notificação de familiares vinculados/).check()
  await b.getByRole('button', { name: 'Criar conta' }).click()
  await expect(b.getByRole('heading', { name: 'Maria Moradora' })).toBeVisible()

  // Cadastra a residência escolhendo o ponto no mapa (funciona sem CEP/geocodificação).
  await b.getByRole('link', { name: 'Adicionar local' }).click()
  await b.getByRole('tab', { name: 'No mapa' }).click()
  const mapa = b.getByRole('application', { name: /Mapa para escolher o local/ })
  await mapa.click({ position: { x: 200, y: 160 } })
  await expect(b.getByText(/Marcador:/)).toBeVisible()
  await b.getByLabel('Confirmo que o marcador está no lugar certo').check()
  await b.getByLabel('Nome do local').fill('Minha casa')
  await b.getByLabel(/Eu moro aqui/).check()
  await b.getByLabel('Moradores', { exact: true }).fill('3')
  await b.getByLabel('Crianças').fill('1')
  await b.getByRole('button', { name: 'Salvar local' }).click()
  await expect(b.getByRole('heading', { name: 'Minha casa' })).toBeVisible()
  await b.getByRole('button', { name: 'Gerar código de convite' }).click()
  const codigo = (await b.locator('.select-all').textContent())!.trim()
  expect(codigo).toMatch(/^[A-Z2-9]{5}-[A-Z2-9]{5}$/)

  // Aparelho A — familiar: conta, usa o código.
  const ctxA = await browser.newContext()
  const a = await ctxA.newPage()
  await a.goto('/perfil')
  await a.getByRole('button', { name: 'Criar uma conta' }).click()
  await a.getByLabel('Nome').fill('Ana Filha')
  await a.getByLabel('E-mail').fill(`ana-${sufixo}@teste.alerta.br`)
  await a.getByLabel('Senha').fill('senha-segura-123')
  await a.getByRole('button', { name: 'Criar conta' }).click()
  await a.getByLabel('Código de convite').fill(codigo)
  await a.getByLabel('Como você chama essa casa').fill('Casa da minha mãe')
  await a.getByRole('button', { name: 'Enviar pedido de acompanhamento' }).click()
  await expect(a.getByText('Aguardando aprovação do morador')).toBeVisible()

  // Morador aprova.
  await b.reload()
  await b.getByRole('button', { name: 'Aprovar' }).click()
  await expect(b.getByText('Receber avisos dos meus pedidos de socorro')).toBeVisible()

  // Morador pede socorro (um toque, como sempre).
  await b.goto('/')
  await b.getByRole('button', { name: /Preciso de socorro/ }).first().click()
  await expect(b.getByRole('heading', { name: 'Seu pedido de ajuda foi registrado!' })).toBeVisible({ timeout: 20_000 })
  await expect(b.getByText(/Pedido associado à sua residência/)).toBeVisible({ timeout: 15_000 })

  // Familiar com o app aberto: tela de alarme por escrito (sirene e vibração no aparelho), sem recarregar.
  const alarme = a.getByRole('alertdialog', { name: /Pedido de socorro na residência monitorada/ })
  await expect(alarme).toBeVisible({ timeout: 20_000 })
  await expect(alarme).toContainText('Casa da minha mãe')
  await expect(alarme.getByRole('button', { name: 'Ver detalhes' })).toBeFocused()
  await alarme.getByRole('button', { name: 'Parar alarme' }).click()
  await expect(alarme).toBeHidden()

  // Familiar vê o aviso guardado e abre o pedido com só o permitido.
  await a.reload()
  const aviso = a.getByRole('link', { name: /Pedido de socorro na residência monitorada/ })
  await expect(aviso).toBeVisible({ timeout: 15_000 })
  await expect(aviso).toContainText('"Casa da minha mãe"')
  await aviso.click()
  await expect(a.getByRole('heading', { name: /Pedido de socorro na residência "Casa da minha mãe"/ })).toBeVisible()
  await expect(a.getByText('Posição exata: o morador não compartilhou.')).toBeVisible()
  await expect(a.getByText(/nenhum órgão real/i).first()).toBeVisible()
  await ctxA.close(); await ctxB.close()
})
