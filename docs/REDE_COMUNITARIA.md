# Rede comunitária de socorro

## O que a comunidade vê
Só aparecem pedidos que o **próprio solicitante autorizou** na tela do pedido ("Pedir apoio da comunidade"). Por padrão, nada é publicado. A autorização pode ser parcial e revogada a qualquer momento (`PUT /api/ocorrencias/{id}/compartilhamento`, só o aparelho ou a conta dona).

| Campo | Quando aparece | Forma |
|---|---|---|
| Tipo de emergência | sempre que autorizado | categoria (alagamento/ilhados, deslizamento, desabamento, acesso bloqueado). Risco à segurança pública e dados médicos não são detalhados |
| Pessoas no local | se "incluir pessoas" | número informado |
| Crianças, idosos, mobilidade reduzida | se "incluir pessoas" | **faixas**: 1 · 2 a 4 · 5 ou mais |
| Necessidades | se "incluir necessidades" | água, alimentos, abrigo, transporte assistido, higiene, roupas, outros |
| Horário | sempre | arredondado para baixo em blocos de 15 min |
| Situação e verificação | sempre | texto público (ex.: "Confirmado por central simulada", "Não verificado") |
| Região | sempre | município + área aproximada (círculo) |
| Ofertas em andamento | sempre | contagem |

Nunca aparecem: nome, telefone, relato escrito, referência do local, idades individuais, dados de saúde, identificador do aparelho, coordenadas.

Totais da região só aparecem com **3 ou mais** pedidos compartilhados na busca (evita reidentificar uma família).

Pedidos de contas fictícias (área de demonstração) e retransmissões marcadas como demonstração ficam fora da lista pública; a lista demonstrativa só aparece com o código da equipe.

## Três representações da localização
| Quem | O que vê |
|---|---|
| Solicitante | o ponto registrado no pedido + **círculo de precisão do GPS** + a área que a comunidade vê (tracejada) |
| Público | **área aproximada**: centro de uma célula de grade de 0,002° (~220 m), a no máximo ~160 m do ponto, e raio = máx(400 m, 160 m + precisão do GPS), arredondado a 100 m. (Até 13/11 04:40 a célula era de ~550 m e o raio mínimo de 600 m; foi reduzida porque o círculo parecia distante da origem.) O círculo **sempre contém** o ponto registrado (teste com 5.000 pontos aleatórios). O centro **não é** a posição da pessoa, e a tela diz isso |
| Operador autorizado (central para a qual o pedido foi encaminhado) | coordenadas, precisão, trilha, relato; acesso auditado |

A posição verdadeira (latitude, longitude, horário, precisão) continua só no backend.

A busca da comunidade é **centrada no GPS de quem consulta** (raio de 2 a 20 km, linha azul tracejada no mapa). A distância exibida é até a **borda** da área, não até o ponto. A posição de quem consulta vai só na consulta (arredondada a 4 casas) e não é armazenada.

### Dois aparelhos na mesma sala
Leituras são comparadas pelas margens: compatíveis se `distância ≤ precisão A + precisão B + 10 m` (sem precisão informada, 50 m). O painel do operador mostra a comparação entre a posição do emissor e a do aparelho que retransmitiu.

## "Quero ajudar": oferta → aceite → encaminhamento → acompanhamento
1. **Oferta**: o voluntário escolhe um pedido compartilhado com necessidades, descreve o que oferece e confirma "Li as orientações e não vou entrar em áreas perigosas" (obrigatório no servidor). Contato é opcional e só vai para a equipe, com autorização.
2. **Aceite**: a central responsável pelo pedido aceita ou recusa (outra central recebe 403).
3. **Encaminhamento**: a equipe escreve a orientação (ponto seguro, horário, o que levar). É o único caminho por onde detalhes chegam ao voluntário.
4. **Acompanhamento**: o voluntário informa entrega concluída ou cancela; cada passo fica no histórico da oferta e do pedido.

Não é despacho de resgate: o voluntário nunca recebe o endereço da família pela plataforma, e todas as telas repetem que não se deve entrar em água, correnteza, encostas ou imóveis danificados. O solicitante não pode ofertar ao próprio pedido; um voluntário não abre duas ofertas para o mesmo pedido.

## Endpoints
- `GET /api/comunidade/pedidos?lat&lon&raioKm&municipio&incluirDemonstrativos`
- `PUT /api/ocorrencias/{id}/compartilhamento`
- `POST /api/comunidade/pedidos/{id}/ofertas`
- `POST /api/apoio/ofertas/{id}/acompanhamento`
- `GET /api/painel/ofertas-comunitarias`, `POST /api/painel/ofertas/{id}/decisao`, `POST /api/painel/ofertas/{id}/encaminhamento` (operador, auditado)

Testes: `backend/tests/Integracao/ComunidadeTestes.cs`, `backend/tests/Unitarios/VisaoComunitariaTestes.cs`, E2E "comunidade vê resumo autorizado…".
