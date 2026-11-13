# Leitura por voz no iPhone — diagnóstico, correção e procedimento de teste

## Causas investigadas (implementação anterior)
| Ponto | Problema no Safari/iOS | Correção (`apps/web/src/lib/fala.ts`) |
|---|---|---|
| `cancel()` seguido de `speak()` | O WebKit descarta a fala nova | Só cancela se houver fala ativa; nesse caso inicia no tique seguinte |
| Gesto do usuário | `speak()` fora do toque é bloqueado até a primeira fala | Primeira fala síncrona dentro do `onClick` |
| Vozes | `getVoices()` vazio no início; `voiceschanged` irregular | Carrega ao iniciar e no evento; sem lista, usa só `lang = 'pt-BR'` |
| Coleta de lixo | Utterance sem referência perde `onend` | Referências mantidas no módulo |
| Texto longo / pause-resume | Fala para no meio; `resume()` instável | Frases ≤ 180 caracteres; pausar guarda a frase e continuar recomeça dela |
| Vários leitores na tela | Disputa pelo motor global | Gerenciador único: só um leitor ativo; troca de página e app em segundo plano param/pausam |
| Sem voz pt-BR / fala muda / falha | Nada acontece e o usuário não sabe | Vigia: sem `onstart` em 2,5 s ou `onerror` → **voz do servidor** (espeak-ng em `/api/voz`, sem serviço externo) num `<audio>` desbloqueado no mesmo toque; erros visíveis com "Tentar novamente" |
| Modo silencioso | A síntese do sistema pode ficar muda com a chave lateral | `<audio>` (mídia) toca mesmo no silencioso; opção "Voz do servidor (funciona no silencioso)" no botão **Aa** |

Privacidade: a voz do servidor recebe apenas o texto público exibido (avisos, situação da região, orientações); nada de nome, telefone ou identificadores.

## Estado
- Automatizado (Vitest, com simulação do motor de fala): fala no toque, frases, pausa/retomada, repetição, velocidade, reserva do servidor quando a fala não inicia — **passa**.
- Voz do servidor: endpoint testado em integração (WAV gerado pelo espeak-ng).
- **iPhone físico: PENDENTE.** Não houve acesso a um iPhone durante o desenvolvimento. Não considerar corrigido até o procedimento abaixo passar.

## Procedimento no iPhone (≈ 5 min)
Registre modelo, versão do iOS, Safari ou app instalado, e a posição da chave de silencioso.
1. Volume em ~50%. Abra https://alerta-em-acao.onrender.com no **Safari**. Ative a localização.
2. Início → "Clima e risco na região" → **Ouvir situação da região**. Deve ouvir em até 3 s. Anote se veio a voz do aparelho ou a mensagem "Voz do servidor do Alerta em Ação".
3. Toque **Pausar** → silêncio; **Continuar** → retoma da frase atual; **Repetir** → recomeça; **Parar**.
4. Troque **Velocidade** para Lenta e ouça de novo (mais lento).
5. Com a leitura tocando, vá para **Mapa** → a leitura deve parar.
6. Mapa → **Ouvir situação da região**; Alertas → **Ouvir aviso** (se houver); Início → **Ouvir orientações**.
7. **Modo silencioso** (chave lateral ligada): repita o passo 2. Se não ouvir, toque **Aa** → Voz da leitura → "Voz do servidor" e repita. Anote os dois resultados.
8. Instale pela Tela de Início (Compartilhar → Adicionar à Tela de Início), abra pelo ícone e repita os passos 2, 3 e 7.
9. Repita 2–4 no tablet Android (Chrome) e no navegador do computador.

Resultado esperado: som audível em todos os casos (voz do aparelho ou do servidor). Se o passo 2 falhar e a mensagem de erro aparecer, copie o texto do erro para a matriz de testes.
