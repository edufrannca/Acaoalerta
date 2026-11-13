import { useCallback, useEffect, useState } from 'react'
import { Link } from 'react-router'
import { AvisoSimulado, Botao, Cartao, Selo, Vazio, classeEntrada } from '../componentes/Base'
import { Mapa } from '../componentes/Mapa'
import { api, formatarHora, formatarRelativo } from '../lib/api'
import { useDados } from '../lib/hooks'
import { MUNICIPIOS, nomeMunicipio, STATUS_ENCAMINHAMENTO } from '../lib/municipios'
import { useSessao } from '../lib/sessao'
import { useEvento } from '../lib/tempoReal'

interface ItemLista {
  id: string; status: string; recebidaNoServidorEm: string; municipioCodigo: string | null; latitude: number | null; longitude: number | null
  localizacaoAproximada: boolean; canal: string; verificacao: string; pessoas: number | null; nivel: string | null
  sinalizacoes: { codigo: string; nivel: string; titulo: string }[] | null; statusIA: string | null
  encaminhamentos: { central: string; status: string; protocolo: string | null }[] | null
}
interface Lista { aviso: string; escopo: string; ocorrencias: ItemLista[] }

interface Evidencia { campo: string; trecho: string }
interface OfertaEquipe { id: string; tipo: string; descricao: string; disponibilidade: string | null; status: string; criadaEm: string; historico: { em: string; descricao: string }[]; instrucaoEquipe: string | null; contatoAutorizado: string | null }

interface Detalhe {
  aviso: string
  ocorrencia: {
    id: string; status: string; statusDescricao: string; recebidaNoServidorEm: string; criadaNoDispositivoEm: string; municipioCodigo: string | null; canal: string
    retransmitidaPor: string | null; verificacao: string; autenticado: boolean
    localizacao: { latitude: number; longitude: number; precisaoMetros: number | null; lidaEm: string; aproximada: boolean } | null
    complemento: Record<string, unknown> | null
    historico: { em: string; tipo: string; descricao: string; ator: string | null }[]
    localizacaoInformada?: boolean
    posicaoRetransmissor?: { latitude: number; longitude: number; precisaoMetros: number | null; lidaEm: string; origem: string; observacao: string
      comparacao: { distanciaMetros: number; margemMetros: number; compativeis: boolean; descricao: string } | null } | null
    compartilhamento?: { autorizado: boolean; incluirPessoas: boolean; incluirNecessidades: boolean }
  }
  ofertasComunitarias?: OfertaEquipe[]
  analise: {
    versaoRegras: string; nivelMaximo: string | null; regrasAvaliadasEm: string; observacao: string
    sinalizacoes: { codigo: string; nivel: string; titulo: string; explicacao: string; evidencias: Evidencia[] }[]
    fontes: { nome: string; atualizadoEm: string | null; oficial: boolean }[]
    ia: { status: string; modelo: string | null; problemas: string[]; iaConcluidaEm: string | null; resultado: {
      resumo: string; informacoesExtraidas: { descricao: string; evidencia: string }[]; possiveisPerigos: { tipo: string; descricao: string; evidencia: string }[]
      necessidadesAssistencia: string[]; indiciosAgravamento: string[]; informacoesAusentes: string[]; informacoesNaoVerificadas: string[]
      sinalizacoesParaAvaliacaoHumana: { codigo: string; explicacao: string; evidencia: string }[]
      pessoasIdentificadas?: { idade: number | null; condicoes: string[] | null; evidencia: string }[] | null } | null; duracaoIAMs?: number | null }
  } | null
  confirmacoes?: { nivel: number; descricao: string; identificadorMensagem: string | null; origem: string; destino: string; horario: string; canal: string; resultado: string; comprovante: string | null }[]
  encaminhamentos: { id: string; tipo: string; central: string; centralNome: string | null; justificativa: string; status: string; tentativas: number; protocolo: string | null
    confirmadoEm: string | null; ultimoErro: string | null; proximaTentativaEm: string; estadoNaCentral: string | null
    historicoTentativas: { numero: number; iniciadaEm: string; duracaoMs: number; httpStatus: number | null; resultado: string; detalhe: string | null }[] }[]
}

const NIVEL: Record<string, { rotulo: string; tom: 'erro' | 'atencao' | 'neutro' }> = {
  IndicioAmeacaImediata: { rotulo: 'Indício de ameaça imediata', tom: 'erro' },
  Atencao: { rotulo: 'Atenção', tom: 'atencao' },
  Informativo: { rotulo: 'Informativo', tom: 'neutro' },
}

const STATUS_OC: Record<string, string> = {
  RecebidaPeloServidor: 'Recebida, aguardando centrais', ConfirmadaParcialmente: 'Confirmada parcialmente', ConfirmadaPorTodas: 'Confirmada por todas',
  FalhaNoEncaminhamento: 'Falha no encaminhamento', SemDestinoConfigurado: 'Sem destino', EmAtendimentoSimulado: 'Em atendimento (fictício)', Encerrada: 'Encerrada',
}

export function Painel() {
  const { usuario } = useSessao()
  const [municipio, setMunicipio] = useState('')
  const [selecionada, setSelecionada] = useState<string | null>(null)
  const lista = useDados<Lista>(usuario ? `/api/painel/ocorrencias${municipio ? `?municipio=${municipio}` : ''}` : null, { intervaloMs: 20000 })
  const [atualizadoEm, setAtualizadoEm] = useState<string | null>(null)

  const aoEvento = useCallback(() => { void lista.recarregar(); setAtualizadoEm(new Date().toISOString()) }, [lista])
  const conectado = useEvento('ocorrenciaAtualizada', aoEvento)

  if (!usuario || !['OperadorCentral', 'Administrador'].includes(usuario.papel))
    return <Cartao titulo="Painel de coordenação"><p>Entre com um perfil de operador ou administrador em <Link className="text-oceano underline" to="/perfil">Perfil</Link>.</p></Cartao>

  const ocorrencias = lista.dados?.ocorrencias ?? []
  const pontos = ocorrencias.filter((o) => o.latitude != null).map((o) => ({ id: o.id, latitude: o.latitude!, longitude: o.longitude!, nivel: o.nivel, rotulo: o.id.slice(0, 8) }))

  return (
    <div className="grid gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-[26px] font-bold text-petroleo">Painel de coordenação</h1>
          <p className="text-[14px] text-tinta-suave">{lista.dados?.escopo}</p>
        </div>
        <div className="flex items-center gap-3">
          <Selo tom={conectado ? 'ok' : 'atencao'}>{conectado ? 'Atualização ao vivo' : 'Ao vivo indisponível, atualizando a cada 20 s'}</Selo>
          {usuario.centralCodigo && <Link to={`/central/${usuario.centralCodigo}`} className="text-[14px] font-medium text-oceano underline-offset-2 hover:underline">Abrir central receptora</Link>}
        </div>
      </div>
      <AvisoSimulado texto="AMBIENTE SIMULADO: NENHUM ÓRGÃO REAL FOI ACIONADO. Ocorrências e centrais são demonstrativas." />

      <div className="grid gap-4 lg:grid-cols-[380px_1fr]">
        <Cartao titulo={`Ocorrências (${ocorrencias.length})`} acao={atualizadoEm ? <span className="text-[12px] text-tinta-suave">evento {formatarRelativo(atualizadoEm)}</span> : undefined}>
          <select aria-label="Filtrar por município" className={`${classeEntrada} mb-3`} value={municipio} onChange={(e) => setMunicipio(e.target.value)}>
            <option value="">Todos os municípios</option>
            {Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
          </select>
          {ocorrencias.length === 0 ? <Vazio>Nenhuma ocorrência encaminhada.</Vazio> : (
            <ul className="grid max-h-[70vh] gap-2 overflow-y-auto pr-1">
              {ocorrencias.map((o) => {
                const n = o.nivel ? NIVEL[o.nivel] : null
                const conf = o.encaminhamentos?.filter((e) => e.status === 'Recebido').length ?? 0
                return (
                  <li key={o.id}>
                    <button onClick={() => setSelecionada(o.id)} data-testid="ocorrencia-painel"
                      className={`w-full rounded-controle p-3 text-left ring-1 ${selecionada === o.id ? 'bg-[#E4EEEE] ring-oceano' : 'bg-areia ring-transparent hover:ring-linha'}`}>
                      <div className="flex flex-wrap items-center gap-2">
                        {n && <Selo tom={n.tom}>{n.rotulo}</Selo>}
                        <span className="text-[12.5px] text-tinta-suave">{formatarRelativo(o.recebidaNoServidorEm)}</span>
                      </div>
                      <p className="mt-1 font-semibold text-petroleo">{nomeMunicipio(o.municipioCodigo)}{o.pessoas ? `, ${o.pessoas} pessoa(s)` : ''}</p>
                      <p className="text-[13px]">{(o.sinalizacoes ?? []).filter((s) => s.nivel !== 'Informativo').map((s) => s.titulo).join('; ') || 'Sem sinalizações de risco'}</p>
                      <p className="mt-1 text-[12.5px] text-tinta-suave">{STATUS_OC[o.status] ?? o.status}, {conf}/{o.encaminhamentos?.length ?? 0} centrais{o.canal !== 'Internet' ? `, via ${o.canal}` : ''}</p>
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </Cartao>
        <div className="grid content-start gap-4">
          <Mapa camadas={['limites', 'avisos', 'chuva']} municipio={municipio || null} pontosOperador={pontos} aoSelecionarPonto={setSelecionada} altura="h-[320px]" mostrarLegenda={false} />
          {selecionada ? <DetalheOcorrencia id={selecionada} versao={atualizadoEm} admin={usuario.papel === 'Administrador'} central={usuario.centralCodigo ?? null} /> : <Vazio>Selecione uma ocorrência na lista ou no mapa.</Vazio>}
        </div>
      </div>
    </div>
  )
}

function DetalheOcorrencia({ id, versao, admin, central }: { id: string; versao: string | null; admin: boolean; central: string | null }) {
  const [d, setD] = useState<Detalhe | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const carregar = useCallback(async () => { try { setD(await api<Detalhe>(`/api/painel/ocorrencias/${id}`)); setErro(null) } catch (e) { setErro(e instanceof Error ? e.message : 'Erro') } }, [id])
  useEffect(() => { void carregar() }, [carregar, versao])

  if (erro) return <Cartao><p role="alert">{erro}</p></Cartao>
  if (!d) return <Cartao><p>Carregando…</p></Cartao>
  const o = d.ocorrencia
  const c = o.complemento ?? {}
  const sim = (k: string) => c[k] === true

  const verificar = async (v: string) => { await api(`/api/painel/ocorrencias/${id}/verificacao`, { method: 'POST', json: { verificacao: v, nota: null } }); void carregar() }
  const reenviar = async (eid: string) => { await api(`/api/painel/encaminhamentos/${eid}/reenviar`, { method: 'POST' }); setTimeout(carregar, 2500) }

  return (
    <div className="grid gap-4">
      <Cartao titulo={`Ocorrência ${o.id.slice(0, 8)}`} acao={<Selo tom="espera">{STATUS_OC[o.status] ?? o.status}</Selo>}>
        <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-[14px] sm:grid-cols-3">
          <div><dt className="text-tinta-suave">Município</dt><dd className="font-medium">{nomeMunicipio(o.municipioCodigo)}</dd></div>
          <div><dt className="text-tinta-suave">Pedido no aparelho</dt><dd className="numero-tabular font-medium">{formatarHora(o.criadaNoDispositivoEm)}</dd></div>
          <div><dt className="text-tinta-suave">Recebido no servidor</dt><dd className="numero-tabular font-medium">{formatarHora(o.recebidaNoServidorEm)}</dd></div>
          <div><dt className="text-tinta-suave">Localização do solicitante</dt><dd className="numero-tabular font-medium">{o.localizacao ? `${o.localizacao.latitude.toFixed(5)}, ${o.localizacao.longitude.toFixed(5)}${o.localizacao.aproximada ? ' (aproximada)' : ` ±${o.localizacao.precisaoMetros ?? '?'} m`}` : 'não informada pelo aparelho emissor'}</dd></div>
          <div><dt className="text-tinta-suave">Última posição</dt><dd className="numero-tabular font-medium">{o.localizacao ? formatarHora(o.localizacao.lidaEm) : '—'}</dd></div>
          <div><dt className="text-tinta-suave">Canal</dt><dd className="font-medium">{o.canal}{o.retransmitidaPor ? ` por ${o.retransmitidaPor}` : ''}</dd></div>
        </dl>
        {o.posicaoRetransmissor && (
          <div className="mt-3 rounded-controle bg-oceano-suave p-3 text-[14px]">
            <p className="font-semibold text-petroleo">Aparelho que retransmitiu ({o.posicaoRetransmissor.origem === 'gps_gateway' ? 'Nearby' : 'QR'})</p>
            <p className="numero-tabular">{o.posicaoRetransmissor.latitude.toFixed(5)}, {o.posicaoRetransmissor.longitude.toFixed(5)} ±{o.posicaoRetransmissor.precisaoMetros ?? '?'} m, {formatarHora(o.posicaoRetransmissor.lidaEm)}</p>
            <p className="text-[13px]">{o.posicaoRetransmissor.observacao}</p>
            {o.posicaoRetransmissor.comparacao && <p className={`mt-1 text-[13px] font-medium ${o.posicaoRetransmissor.comparacao.compativeis ? 'text-seguro' : 'text-atencao'}`}>{o.posicaoRetransmissor.comparacao.descricao}</p>}
          </div>
        )}
        {o.compartilhamento && <p className="mt-2 text-[13px] text-tinta-suave">Comunidade: {o.compartilhamento.autorizado ? `resumo autorizado (pessoas: ${o.compartilhamento.incluirPessoas ? 'sim' : 'não'}, necessidades: ${o.compartilhamento.incluirNecessidades ? 'sim' : 'não'})` : 'não compartilhado'}.</p>}
        {admin && <p className="mt-2 text-[13px] text-tinta-suave">Visão de administrador: localização aproximada e sem relato (minimização de dados).</p>}
        {o.complemento && (
          <div className="mt-3 rounded-controle bg-areia p-3 text-[14px]">
            <p className="font-semibold text-petroleo">Informado pelo cidadão</p>
            <p>
              {[c.quantidadePessoas && `${c.quantidadePessoas} pessoa(s)`, c.criancas && `${c.criancas} criança(s)`, c.idosos && `${c.idosos} idoso(s)`, c.pessoasFeridas && `${c.pessoasFeridas} ferido(s)`, c.animais && `${c.animais} animal(is) de estimação`,
                sim('ilhado') && 'ilhado', sim('aguaSubindo') && 'água subindo', sim('correnteza') && 'correnteza', sim('riscoDeslizamento') && 'risco de deslizamento',
                sim('desmoronamento') && 'desabamento', sim('acessoBloqueado') && 'acesso bloqueado', sim('mobilidadeReduzida') && 'mobilidade reduzida',
                sim('equipamentoMedicoEssencial') && 'equipamento médico', sim('sozinho') && 'sozinho(a)'].filter(Boolean).join(', ') || 'Sem dados estruturados'}
            </p>
            {Array.isArray(c.pessoas) && <p className="text-[13px]">Idades: {(c.pessoas as { idade: number | null }[]).map((p) => p.idade ?? '?').join(', ')}</p>}
            {typeof c.descricao === 'string' && <p className="mt-1">“{c.descricao}”</p>}
            {typeof c.referenciaLocal === 'string' && <p className="mt-1 text-[13px]">Referência: {c.referenciaLocal}</p>}
          </div>
        )}
        {!admin && (
          <div className="mt-3 flex flex-wrap gap-2">
            <Botao variante="secundario" onClick={() => verificar('VerificadaPorOperador')}>Marcar como verificada</Botao>
            <Botao variante="fantasma" onClick={() => verificar('DescartadaPorOperador')}>Descartar (relato falso)</Botao>
            <span className="self-center text-[13px] text-tinta-suave">Situação: {o.verificacao}</span>
          </div>
        )}
      </Cartao>

      {(d.ofertasComunitarias?.length ?? 0) > 0 && <OfertasComunitarias ofertas={d.ofertasComunitarias!} aoMudar={carregar} />}

      {d.analise && (
        <Cartao titulo="Análise situacional" acao={d.analise.nivelMaximo ? <Selo tom={NIVEL[d.analise.nivelMaximo]?.tom ?? 'neutro'}>{NIVEL[d.analise.nivelMaximo]?.rotulo}</Selo> : undefined}>
          <p className="mb-3 text-[13px] text-tinta-suave">{d.analise.observacao} Regras {d.analise.versaoRegras}, avaliadas {formatarHora(d.analise.regrasAvaliadasEm)}.</p>
          <ul className="grid gap-2">
            {d.analise.sinalizacoes.map((s) => (
              <li key={s.codigo} className="rounded-controle bg-areia p-3">
                <div className="flex flex-wrap items-center gap-2"><Selo tom={NIVEL[s.nivel]?.tom ?? 'neutro'}>{s.titulo}</Selo><code className="text-[12px] text-tinta-suave">{s.codigo}</code></div>
                <p className="mt-1 text-[14px]">{s.explicacao}</p>
                {s.evidencias.length > 0 && <p className="mt-1 text-[13px] text-tinta-suave">Evidência: {s.evidencias.map((e) => `“${e.trecho}”`).join('; ')}</p>}
              </li>
            ))}
          </ul>
          <div className="mt-4 border-t border-linha pt-3">
            <p className="font-semibold text-petroleo">Inteligência artificial <Selo tom={d.analise.ia.status === 'Concluida' ? 'ok' : d.analise.ia.status === 'NaoConfigurada' ? 'neutro' : 'atencao'}>{d.analise.ia.status}</Selo></p>
            {d.analise.ia.status === 'NaoConfigurada' && <p className="text-[13.5px] text-tinta-suave">Nenhum provedor de IA configurado. As regras determinísticas acima funcionam sem IA.</p>}
            {d.analise.ia.resultado && (
              <div className="mt-2 grid gap-2 text-[14px]">
                <p>{d.analise.ia.resultado.resumo}</p>
                {d.analise.ia.resultado.informacoesExtraidas.length > 0 && <p><span className="font-medium">Extraído:</span> {d.analise.ia.resultado.informacoesExtraidas.map((f) => `${f.descricao} (“${f.evidencia}”)`).join('; ')}</p>}
                {(d.analise.ia.resultado.pessoasIdentificadas?.length ?? 0) > 0 && <p><span className="font-medium">Pessoas citadas:</span> {d.analise.ia.resultado.pessoasIdentificadas!.map((p) => `${p.idade ?? 'idade não informada'}${p.condicoes?.length ? ` (${p.condicoes.join(', ')})` : ''}`).join('; ')} <span className="text-[12px] text-tinta-suave">— descreve assistência, não define prioridade</span></p>}
                {d.analise.ia.resultado.possiveisPerigos.length > 0 && <p><span className="font-medium">Possíveis perigos:</span> {d.analise.ia.resultado.possiveisPerigos.map((p) => `${p.descricao} (“${p.evidencia}”)`).join('; ')}</p>}
                {d.analise.ia.resultado.sinalizacoesParaAvaliacaoHumana.length > 0 && <p><span className="font-medium">Para avaliação humana:</span> {d.analise.ia.resultado.sinalizacoesParaAvaliacaoHumana.map((x) => `${x.codigo}: ${x.explicacao}`).join('; ')}</p>}
                {d.analise.ia.resultado.informacoesAusentes.length > 0 && <p><span className="font-medium">Ausente:</span> {d.analise.ia.resultado.informacoesAusentes.join('; ')}</p>}
                {d.analise.ia.resultado.informacoesNaoVerificadas.length > 0 && <p><span className="font-medium">Não verificado:</span> {d.analise.ia.resultado.informacoesNaoVerificadas.join('; ')}</p>}
                <p className="text-[12.5px] text-tinta-suave">Modelo {d.analise.ia.modelo}, {formatarHora(d.analise.ia.iaConcluidaEm)}{d.analise.ia.duracaoIAMs ? `, ${(d.analise.ia.duracaoIAMs / 1000).toFixed(1)} s` : ''}. A IA não decide resgates. Itens sem evidência literal na entrada são descartados automaticamente.</p>
              </div>
            )}
            {d.analise.ia.problemas.length > 0 && <p className="mt-1 text-[12.5px] text-atencao">{d.analise.ia.problemas.join(' ')}</p>}
          </div>
          {d.analise.fontes.length > 0 && <p className="mt-3 text-[12.5px] text-tinta-suave">Fontes territoriais: {d.analise.fontes.map((f) => `${f.nome}${f.atualizadoEm ? ` (${formatarHora(f.atualizadoEm)})` : ''}`).join('; ')}</p>}
        </Cartao>
      )}

      {(d.confirmacoes?.length ?? 0) > 0 && (
        <Cartao titulo="Níveis de entrega comprovados">
          <ol className="grid gap-1.5 text-[13.5px]" data-testid="confirmacoes">
            {d.confirmacoes!.map((c, i) => (
              <li key={i} className="rounded-controle bg-areia px-3 py-2">
                <span className="font-semibold text-petroleo">Nível {c.nivel}</span> — {c.descricao}
                <span className="block text-[12.5px] text-tinta-suave numero-tabular">{formatarHora(c.horario)} · {c.origem} → {c.destino} · canal {c.canal}{c.comprovante ? ` · comprovante ${c.comprovante}` : ''}{c.identificadorMensagem ? ` · mensagem ${c.identificadorMensagem.slice(0, 8)}` : ''}</span>
              </li>
            ))}
          </ol>
          <p className="mt-2 text-[12.5px] text-tinta-suave">Nível 6 (recebimento por órgão real) não existe neste ambiente: exige integração oficial homologada.</p>
        </Cartao>
      )}

      <Cartao titulo="Encaminhamentos e confirmações">
        <ul className="grid gap-2">
          {d.encaminhamentos.map((e) => {
            const s = STATUS_ENCAMINHAMENTO[e.status] ?? { rotulo: e.status, tom: 'espera' as const }
            const pode = admin || e.central === central
            return (
              <li key={e.id} className="rounded-controle bg-areia p-3 text-[14px]">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span className="font-semibold text-petroleo">{e.centralNome ?? e.central}{e.tipo !== 'Ocorrencia' ? ' (atualização)' : ''}</span>
                  <Selo tom={s.tom}>{s.rotulo}</Selo>
                </div>
                <p className="text-[13px] text-tinta-suave">{e.justificativa}</p>
                {e.protocolo && <p className="numero-tabular">Protocolo {e.protocolo}, confirmado {formatarHora(e.confirmadoEm)}</p>}
                {e.estadoNaCentral && <p>Estado na central: {e.estadoNaCentral}</p>}
                {e.ultimoErro && e.status !== 'Recebido' && <p className="text-emergencia-escura">{e.ultimoErro}</p>}
                {e.status === 'FalhaTemporaria' && <p className="text-[13px]">Próxima tentativa automática {formatarHora(e.proximaTentativaEm)}</p>}
                <details className="mt-1">
                  <summary className="cursor-pointer text-[13px] text-oceano">{e.tentativas} tentativa(s)</summary>
                  <ol className="mt-1 text-[12.5px]">
                    {e.historicoTentativas.map((t) => <li key={t.numero + t.iniciadaEm} className="numero-tabular">#{t.numero} {formatarHora(t.iniciadaEm)}: {t.resultado}, HTTP {t.httpStatus ?? 'sem resposta'}, {t.duracaoMs} ms{t.detalhe ? `, ${t.detalhe}` : ''}</li>)}
                  </ol>
                </details>
                {pode && ['FalhaTemporaria', 'Rejeitado', 'Esgotado'].includes(e.status) && <Botao className="mt-2" variante="secundario" onClick={() => reenviar(e.id)}>Reenviar agora</Botao>}
              </li>
            )
          })}
        </ul>
      </Cartao>

      <Cartao titulo="Histórico">
        <ol className="grid gap-2 border-l-2 border-linha pl-4">
          {o.historico.slice().reverse().map((h, i) => (
            <li key={i} className="text-[14px]"><span className="numero-tabular text-[12.5px] text-tinta-suave">{formatarHora(h.em)}{h.ator ? `, ${h.ator}` : ''}</span><br />{h.descricao}</li>
          ))}
        </ol>
      </Cartao>
    </div>
  )
}

const ROTULO_OFERTA: Record<string, string> = {
  AguardandoAvaliacao: 'Aguardando avaliação', AceitaPelaEquipe: 'Aceita · falta orientar', RecusadaPelaEquipe: 'Recusada', Encaminhada: 'Orientação enviada',
  Concluida: 'Concluída pelo voluntário', Cancelada: 'Cancelada pelo voluntário',
}

/** Ofertas da comunidade: a equipe decide e orienta. O voluntário nunca recebe a posição da família por aqui. */
function OfertasComunitarias({ ofertas, aoMudar }: { ofertas: OfertaEquipe[]; aoMudar: () => void }) {
  const [instrucao, setInstrucao] = useState<Record<string, string>>({})
  const [erro, setErro] = useState<string | null>(null)
  const agir = async (caminho: string, json: unknown) => {
    try { await api(caminho, { method: 'POST', json }); setErro(null) } catch (e) { setErro(e instanceof Error ? e.message : 'Falha') }
    aoMudar()
  }
  return (
    <Cartao titulo={`Ofertas da comunidade (${ofertas.length})`}>
      <p className="mb-2 text-[13.5px] text-tinta-suave">Ajuda comunitária não é resgate. Oriente entregas em ponto seguro (abrigo, escola, posto). Não envie voluntários para áreas de risco.</p>
      {erro && <p role="alert" className="mb-2 text-[14px] text-emergencia-escura">{erro}</p>}
      <ul className="grid gap-2">
        {ofertas.map((f) => (
          <li key={f.id} className="rounded-controle bg-areia p-3 text-[14px]">
            <div className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold">{f.descricao}</span><Selo tom={f.status === 'Encaminhada' || f.status === 'Concluida' ? 'ok' : 'espera'}>{ROTULO_OFERTA[f.status] ?? f.status}</Selo></div>
            <p className="text-[13px] text-tinta-suave">{f.disponibilidade ?? 'Disponibilidade não informada'} · {f.contatoAutorizado ? `contato autorizado: ${f.contatoAutorizado}` : 'sem contato autorizado'}</p>
            {f.instrucaoEquipe && <p className="mt-1 text-[13px]">Orientação enviada: {f.instrucaoEquipe}</p>}
            {f.status === 'AguardandoAvaliacao' && (
              <div className="mt-2 flex flex-wrap gap-2">
                <Botao variante="secundario" onClick={() => agir(`/api/painel/ofertas/${f.id}/decisao`, { aceita: true })}>Aceitar oferta</Botao>
                <Botao variante="fantasma" onClick={() => agir(`/api/painel/ofertas/${f.id}/decisao`, { aceita: false, nota: 'Não necessária no momento' })}>Recusar</Botao>
              </div>
            )}
            {f.status === 'AceitaPelaEquipe' && (
              <div className="mt-2 grid gap-2">
                <label className="block"><span className="text-[13px] font-medium">Orientação ao voluntário (ponto seguro, horário, o que levar)</span>
                  <textarea className="mt-1 min-h-16 w-full rounded-controle border border-linha bg-branco p-2" maxLength={500} value={instrucao[f.id] ?? ''} onChange={(e) => setInstrucao({ ...instrucao, [f.id]: e.target.value })} />
                </label>
                <Botao variante="primario" disabled={(instrucao[f.id] ?? '').trim().length < 5} onClick={() => agir(`/api/painel/ofertas/${f.id}/encaminhamento`, { instrucao: instrucao[f.id] })}>Enviar orientação</Botao>
              </div>
            )}
          </li>
        ))}
      </ul>
    </Cartao>
  )
}
