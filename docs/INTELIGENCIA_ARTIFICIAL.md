# Inteligência artificial

## Princípio
A IA **resume e organiza** um pedido para um operador humano. Ela **não** decide prioridade, não ordena vidas, não usa idade como critério isolado e não dá ordens de evacuação. O sistema funciona completamente sem IA.

## Duas camadas
1. **Regras determinísticas** (`MotorRegrasUrgencia`, versão `regras-2026.11.1`): rodam sempre, na mesma transação do registro. Cada sinalização traz **nível** (indício de ameaça imediata / atenção / informativo), **explicação** e **evidências** (campo + trecho). Exemplos: `AGUA_SUBINDO_COM_ILHAMENTO`, `DESABAMENTO`, `PESSOA_FERIDA`, `NECESSIDADE_ASSISTENCIA` (crianças/idosos/mobilidade: sempre "atenção", nunca ameaça por idade), `INFORMACOES_CONTRADITORIAS`, `LOCALIZACAO_DESCONHECIDA/IMPRECISA/ANTIGA`, `RELATO_INCOMPLETO`, `DADOS_METEOROLOGICOS_DESATUALIZADOS`, `AVISO_OFICIAL_VIGENTE`, `SUSCETIBILIDADE_CONHECIDA`.
2. **Modelo de linguagem** (opcional): roda em segundo plano (`TrabalhoAnaliseIA`), com timeout de 45 s e até 3 tentativas.

## Adaptador independente do fornecedor
`IProvedorIA` com implementações para Anthropic (Messages API) e APIs compatíveis com OpenAI. Configure com `IA__Provedor`, `IA__ChaveApi` e `IA__Modelo`. A entrada enviada é mínima: sem nome, telefone nem identificador do usuário.

## Validação da saída (`ValidadorSaidaIA`)
- JSON no esquema `ResultadoAnaliseIA` (campos obrigatórios; resumo ≤ 800 caracteres).
- **Ancoragem:** cada fato, perigo e sinalização precisa de `evidencia` que exista *literalmente* na entrada (normalizada sem acentos). Itens sem evidência são **descartados** e contados.
- Códigos de sinalização restritos a uma lista fechada.
- `pessoasEnvolvidas` só é aceito se coincidir com um número informado.
- Resposta malformada → `RejeitadaPelaValidacao`; o operador vê as regras e o motivo.

## Avaliação (`tests/AvaliacaoIA`)
- 10 casos fictícios revisados (ilhado com água subindo; criança e idoso; ferido; deslizamento; barreira instável; rua bloqueada; localização desconhecida; relato incompleto; contradição; "idade sozinha não é ameaça").
- Respostas pré-definidas: bem ancorada (aceita), **com invenções** (cachorro, fios caídos, 7 pessoas: tudo descartado), malformadas (rejeitadas), JSON cercado de texto (aceito).
- A aprovação **não depende de outra IA**.
- Teste com modelo real: roda quando `IA__CHAVEAPI` estiver definida (exige ≥ 9/10 respostas válidas). **Na entrega não havia chave configurada: o teste foi ignorado e nenhum modelo real foi avaliado.**

## Provedor do hackathon: Groq
- `IA__Provedor=groq`, `IA__Modelo=openai/gpt-oss-20b` (padrão), endpoint `https://api.groq.com/openai/v1/chat/completions`, `response_format=json_object`,
  `reasoning_effort=low`, `temperature=0`. A chave só existe no backend (`IA__ChaveApi`, variável de ambiente / secret); nunca no app, front, testes ou logs.
- Falhas classificadas (`FalhaProvedorIA`): `limite_requisicoes` (HTTP 429, com Retry-After), `timeout`, `resposta_invalida` (incl. `json_validate_failed`),
  `indisponivel` (5xx). Espera entre tentativas de 15 s, 30 s, 60 s (máx. 4). A ocorrência segue encaminhada pelas regras em qualquer falha.
- Cada complemento do pedido gera nova interpretação (o resultado anterior fica visível até ser substituído).
- Novo campo validado: `pessoasIdentificadas` (idade + condições + evidência). Idade só é aceita se foi informada; condições descrevem assistência, nunca prioridade.
- Painel: resumo, pessoas citadas, perigos, sinalizações com justificativa, ausências, não verificados, modelo e tempo de resposta.

## Inferência real — executada em 13/11/2026 (02:30, Recife)
Ambiente público `https://alerta-em-acao.onrender.com`, pedido FICTÍCIO entregue pelo gateway v2 (Recife, 4 pessoas, idades 3/34/78, água subindo, ilhados):
- Provedor/modelo: `groq/openai/gpt-oss-20b` · tempo de resposta **839 ms** · status **Concluida** · validação do esquema sem problemas, 0 itens descartados.
- Pessoas extraídas: 3, 34 e 78 anos (78 com "andador"), todas com evidência literal; `pessoasEnvolvidas = 4`.
- Perigos com evidência: água subindo rápido; ilhamento. Sinalizações para avaliação humana: AGUA_SUBINDO, ILHAMENTO, PESSOA_ISOLADA, ACESSO_BLOQUEADO, NECESSIDADE_ASSISTENCIA.
- **Erro observado:** o resumo livre disse "outras duas pessoas (34 anos)", mas só uma idade 34 foi informada. O resumo não é ancorado como os demais campos;
  o painel mostra os dados estruturados validados ao lado e a decisão é sempre humana. Melhoria pendente: validar números citados no resumo.
- Regras determinísticas no mesmo pedido: AGUA_SUBINDO_COM_ILHAMENTO (indício de ameaça imediata), NECESSIDADE_ASSISTENCIA, PESSOA_ISOLADA, ACESSO_BLOQUEADO (atenção).

A bateria de 10 casos do CI (`ia-groq`) segue pendente: o GitHub Actions da conta não inicia.
