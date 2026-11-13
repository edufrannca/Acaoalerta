import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router'
import { AvisoSimulado, Botao, Cartao, Selo, classeEntrada } from '../componentes/Base'
import { ContatosEmergencia } from '../componentes/Contatos'
import { api, ErroApi, formatarHora } from '../lib/api'
import { dispositivoId, type ItemFila } from '../lib/dispositivo'
import { descricaoEstado, observarFila, transmitir } from '../lib/filaOffline'
import { useLocalizacao, useOnline } from '../lib/hooks'
import { STATUS_ENCAMINHAMENTO } from '../lib/municipios'
import { anexarPosicaoSePendente, complementar } from '../lib/socorro'
import { useEvento } from '../lib/tempoReal'

interface VisaoServidor {
  id: string
  status: string
  statusDescricao: string
  nivelComprovado?: number
  nivelDescricao?: string
  municipio: { codigo: string; nome: string } | null
  localizacao: { latitude: number; longitude: number; precisaoMetros: number | null; lidaEm: string } | null
  encaminhamentos: { id: string; central: string; status: string; protocolo: string | null; confirmadoEm: string | null; tentativas: number; ultimoErro: string | null; estadoNaCentral: string | null }[]
  historico: { em: string; tipo: string; descricao: string }[]
  aviso: string
}

const SITUACOES: { chave: string; rotulo: string }[] = [
  { chave: 'ilhado', rotulo: 'Estou ilhado' },
  { chave: 'aguaSubindo', rotulo: 'Água subindo' },
  { chave: 'correnteza', rotulo: 'Correnteza' },
  { chave: 'riscoDeslizamento', rotulo: 'Risco de deslizamento' },
  { chave: 'desmoronamento', rotulo: 'Desabamento' },
  { chave: 'barreiraComSinaisInstabilidade', rotulo: 'Barreira com rachaduras' },
  { chave: 'acessoBloqueado', rotulo: 'Acesso bloqueado' },
  { chave: 'equipamentoMedicoEssencial', rotulo: 'Uso equipamento médico' },
  { chave: 'mobilidadeReduzida', rotulo: 'Alguém não consegue andar' },
  { chave: 'sozinho', rotulo: 'Estou sozinho(a)' },
  { chave: 'riscoSegurancaPublica', rotulo: 'Risco à segurança' },
]

export function AcompanharSocorro() {
  const { id = '' } = useParams()
  const online = useOnline()
  const [local, setLocal] = useState<ItemFila | null>(null)
  const [servidor, setServidor] = useState<VisaoServidor | null>(null)
  const { posicao, estado: gps, iniciar } = useLocalizacao(15000)
  const ultimaEnviada = useRef<string | null>(null)

  useEffect(() => observarFila((l) => setLocal(l.find((x) => x.id === id) ?? null)), [id])

  const atualizar = useCallback(async () => {
    try { setServidor(await api<VisaoServidor>(`/api/ocorrencias/${id}`)) } catch (e) { if (!(e instanceof ErroApi && e.status === 404)) { /* offline: mantém último */ } }
  }, [id])

  useEffect(() => { iniciar() }, [iniciar])
  useEffect(() => {
    void atualizar()
    const t = setInterval(atualizar, 5000)
    return () => clearInterval(t)
  }, [atualizar, local?.estado])

  // Acompanhamento em tempo real do próprio pedido.
  useEvento('ocorrenciaAtualizada', (d) => { if ((d as { id?: string }).id === id) void atualizar() },
    (c) => { void dispositivoId().then((disp) => c.invoke('AcompanharOcorrencia', id, disp).catch(() => {})) })

  // Posição: anexa ao pedido se ainda não saiu; depois, atualizações limitadas (bateria).
  useEffect(() => {
    if (!posicao || posicao.lidaEm === ultimaEnviada.current) return
    ultimaEnviada.current = posicao.lidaEm
    void (async () => {
      if (await anexarPosicaoSePendente(id, posicao)) { void transmitir(id); return }
      if (local?.estado === 'recebido_servidor') {
        try { await api(`/api/ocorrencias/${id}/localizacao`, { method: 'POST', json: { latitude: posicao.latitude, longitude: posicao.longitude, precisaoMetros: posicao.precisao, lidaEm: posicao.lidaEm } }) } catch { /* tenta na próxima leitura */ }
      }
    })()
  }, [posicao, id, local?.estado])

  const estadoLocal = local?.estado
  const recebidoServidor = estadoLocal === 'recebido_servidor' || !!servidor
  const confirmadas = servidor?.encaminhamentos.filter((e) => e.status === 'Recebido').length ?? 0

  return (
    <div className="grid gap-4 md:grid-cols-[1.1fr_1fr]">
      <div className="grid content-start gap-4">
        <section aria-live="polite" className={`rounded-cartao p-5 ${recebidoServidor ? 'bg-petroleo text-branco' : 'bg-[#FBF1DC] text-tinta'}`}>
          <h1 className="text-[24px] font-bold leading-tight">
            {!local && !servidor ? 'Pedido não encontrado neste aparelho' : recebidoServidor ? 'Pedido recebido pelo servidor' : 'Pedido salvo neste aparelho. Ainda não transmitido.'}
          </h1>
          <p className={`mt-1 text-[15px] ${recebidoServidor ? 'text-areia/90' : ''}`}>
            {servidor ? servidor.statusDescricao : local ? descricaoEstado[local.estado] + (online ? '' : ' Sem internet: enviaremos assim que a conexão voltar.') : ''}
          </p>
          <p className={`mt-2 text-[14px] font-semibold ${recebidoServidor ? 'text-branco' : ''}`} data-testid="nivel-comprovado">
            Último nível comprovado: {servidor?.nivelComprovado ? `Nível ${servidor.nivelComprovado} — ${servidor.nivelDescricao}` : 'Nível 1 — salvo neste aparelho'}
          </p>
          {servidor?.nivelComprovado === 5 && <p className="text-[12.5px] text-areia/80">Central SIMULADA de demonstração. Nenhum órgão real foi acionado.</p>}
          {local?.ultimoErro && !recebidoServidor && <p className="mt-1 text-[13.5px]">Última tentativa: {local.ultimoErro} ({local.tentativas}x)</p>}
          {!recebidoServidor && local && <Botao className="mt-3" variante="primario" onClick={() => transmitir(id)}>Tentar enviar agora</Botao>}
          {servidor && <p className="mt-2 text-[13px] text-areia/80">Identificador do pedido: <span className="numero-tabular">{servidor.id.slice(0, 8)}</span>{servidor.municipio ? `, ${servidor.municipio.nome}` : ''}</p>}
        </section>

        <Cartao titulo="Ligue também">
          <p className="mb-2 text-[14.5px]">Se houver risco imediato, ligue agora. O envio digital não substitui a ligação.</p>
          <ContatosEmergencia compacto destaque={['193', '199', '192', '190']} />
        </Cartao>

        <Complemento id={id} />
      </div>

      <div className="grid content-start gap-4">
        <Cartao titulo="Para quem foi encaminhado" acao={servidor ? <Selo tom={confirmadas === servidor.encaminhamentos.length && confirmadas > 0 ? 'ok' : 'espera'}>{confirmadas}/{servidor.encaminhamentos.length} confirmações</Selo> : undefined}>
          <AvisoSimulado texto="Centrais simuladas: nenhum órgão real recebe este pedido na demonstração." />
          {!servidor ? <p className="mt-3 text-[14px] text-tinta-suave">Os destinos aparecem quando o servidor receber o pedido.</p> : (
            <ul className="mt-3 grid gap-2">
              {servidor.encaminhamentos.length === 0 && <li className="text-[14px]">Sem central configurada para este local. Ligue 193 ou 199.</li>}
              {servidor.encaminhamentos.map((e) => {
                const s = STATUS_ENCAMINHAMENTO[e.status] ?? { rotulo: e.status, tom: 'espera' as const }
                return (
                  <li key={e.id} className="rounded-controle bg-areia p-3">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span className="font-semibold text-petroleo">{e.central}</span>
                      <Selo tom={s.tom}>{s.rotulo}</Selo>
                    </div>
                    {e.protocolo && <p className="numero-tabular mt-1 text-[13.5px]">Protocolo demonstrativo {e.protocolo}, {formatarHora(e.confirmadoEm)}</p>}
                    {e.ultimoErro && e.status !== 'Recebido' && <p className="mt-1 text-[13px] text-emergencia-escura">{e.ultimoErro} (tentativa {e.tentativas})</p>}
                    {e.estadoNaCentral && e.estadoNaCentral !== 'Recebido' && <p className="mt-1 text-[13px]">Estado na central simulada: {e.estadoNaCentral}</p>}
                  </li>
                )
              })}
            </ul>
          )}
        </Cartao>

        <Cartao titulo="Sua localização para a equipe">
          {posicao ? (
            <p className="text-[14.5px]">Enviando atualizações enquanto esta tela estiver aberta (no máximo a cada 15 s). Última: {formatarHora(posicao.lidaEm)}, precisão ±{posicao.precisao ?? '?'} m.</p>
          ) : (
            <p className="text-[14.5px]">{gps === 'negado' ? 'Localização negada. Descreva uma referência no campo ao lado.' : 'Obtendo localização…'}</p>
          )}
          {servidor?.localizacao && <p className="mt-1 text-[13px] text-tinta-suave">No servidor: {servidor.localizacao.latitude.toFixed(5)}, {servidor.localizacao.longitude.toFixed(5)} ({formatarHora(servidor.localizacao.lidaEm)})</p>}
        </Cartao>

        {servidor && (
          <Cartao titulo="Histórico">
            <ol className="grid gap-2 border-l-2 border-linha pl-4">
              {servidor.historico.slice().reverse().map((h, i) => (
                <li key={i} className="text-[14px]"><span className="numero-tabular text-[12.5px] text-tinta-suave">{formatarHora(h.em)}</span><br />{h.descricao}</li>
              ))}
            </ol>
          </Cartao>
        )}
      </div>
    </div>
  )
}

function Complemento({ id }: { id: string }) {
  const [marcados, setMarcados] = useState<Record<string, boolean>>({})
  const [pessoas, setPessoas] = useState({ quantidadePessoas: '', criancas: '', idosos: '', pessoasFeridas: '', idadeSolicitante: '' })
  const [descricao, setDescricao] = useState('')
  const [referencia, setReferencia] = useState('')
  const [enviado, setEnviado] = useState<string | null>(null)

  const enviar = async () => {
    const num = (v: string) => (v === '' ? undefined : Number(v))
    const corpo: Record<string, unknown> = { ...Object.fromEntries(Object.entries(marcados).filter(([, v]) => v)), descricao: descricao || undefined, referenciaLocal: referencia || undefined }
    for (const [k, v] of Object.entries(pessoas)) if (num(v) !== undefined) corpo[k] = num(v)
    await complementar(id, corpo)
    setEnviado(new Date().toISOString())
  }

  const contador = (chave: keyof typeof pessoas, rotulo: string) => (
    <label className="block">
      <span className="text-[13.5px] font-medium">{rotulo}</span>
      <input inputMode="numeric" pattern="[0-9]*" min={0} max={500} type="number" className={classeEntrada} value={pessoas[chave]}
        onChange={(e) => setPessoas({ ...pessoas, [chave]: e.target.value })} />
    </label>
  )

  return (
    <Cartao titulo="Conte o que está acontecendo">
      <p className="mb-3 text-[14px] text-tinta-suave">Opcional. O pedido já foi registrado; cada detalhe ajuda a equipe a se preparar.</p>
      <fieldset>
        <legend className="mb-2 text-[14px] font-medium">Marque o que se aplica</legend>
        <div className="flex flex-wrap gap-2">
          {SITUACOES.map((s) => (
            <button key={s.chave} type="button" aria-pressed={!!marcados[s.chave]} onClick={() => setMarcados({ ...marcados, [s.chave]: !marcados[s.chave] })}
              className={`min-h-11 rounded-full px-4 text-[14.5px] font-medium ring-1 ${marcados[s.chave] ? 'bg-emergencia text-branco ring-emergencia' : 'bg-branco text-tinta ring-linha'}`}>
              {s.rotulo}
            </button>
          ))}
        </div>
      </fieldset>
      <div className="mt-4 grid grid-cols-2 gap-3 sm:grid-cols-3">
        {contador('quantidadePessoas', 'Pessoas no local')}
        {contador('criancas', 'Crianças')}
        {contador('idosos', 'Pessoas idosas')}
        {contador('pessoasFeridas', 'Feridos')}
        {contador('idadeSolicitante', 'Sua idade')}
      </div>
      <label className="mt-3 block">
        <span className="text-[13.5px] font-medium">Descreva em poucas palavras</span>
        <textarea className={`${classeEntrada} min-h-20`} value={descricao} onChange={(e) => setDescricao(e.target.value)} maxLength={2000} placeholder="Ex.: estamos no primeiro andar, a água chegou na escada" />
      </label>
      <label className="mt-3 block">
        <span className="text-[13.5px] font-medium">Referência do local</span>
        <input className={classeEntrada} value={referencia} onChange={(e) => setReferencia(e.target.value)} maxLength={300} placeholder="Ex.: casa azul ao lado da igreja" />
      </label>
      <Botao className="mt-4 w-full" variante="perigo" onClick={enviar}>Enviar informações</Botao>
      {enviado && <p role="status" className="mt-2 text-[14px] text-seguro">Informações salvas no aparelho e na fila de envio ({formatarHora(enviado)}). Acompanhe o estado em Meus pedidos.</p>}
    </Cartao>
  )
}
