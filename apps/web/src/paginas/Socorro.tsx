import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router'
import { AvisoSimulado, Botao, Cartao, Selo, classeEntrada } from '../componentes/Base'
import { ContatosEmergencia } from '../componentes/Contatos'
import { api, ErroApi, formatarHora } from '../lib/api'
import { dispositivoId, type ItemFila } from '../lib/dispositivo'
import { descricaoEstado, observarFila, transmitir } from '../lib/filaOffline'
import { useLocalizacao, useOnline } from '../lib/hooks'
import { STATUS_ENCAMINHAMENTO } from '../lib/municipios'
import { anexarPosicaoSePendente, complementar } from '../lib/socorro'
import { useEvento } from '../lib/tempoReal'
import { Mapa } from '../componentes/Mapa'
import { EtapasEntrega, TransmitirProximo } from '../componentes/TransmitirProximo'
import { NECESSIDADES, type Compartilhamento } from '../lib/comunidade'
import { enfileirar } from '../lib/filaOffline'
import { uuid } from '../lib/dispositivo'
import { useSessao } from '../lib/sessao'

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
  canal?: string
  compartilhamento?: Compartilhamento & { atualizadoEm: string | null }
  necessidades?: string[]
  areaPublica?: { centroLatitude: number; centroLongitude: number; raioMetros: number } | null
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

  const [proximo, setProximo] = useState(false)
  const [codigoExibido, setCodigoExibido] = useState(false)
  const marcarExibido = useCallback(() => setCodigoExibido(true), [])
  const estadoLocal = local?.estado
  const recebidoServidor = estadoLocal === 'recebido_servidor' || !!servidor
  const confirmadas = servidor?.encaminhamentos.filter((e) => e.status === 'Recebido').length ?? 0

  return (
    <div className="grid gap-4 md:grid-cols-[1.1fr_1fr]">
      <div className="grid content-start gap-4">
        <section aria-live="polite" className={`rounded-cartao p-5 ${recebidoServidor ? 'bg-petroleo text-branco' : 'bg-[#FBF1DC] text-tinta'}`}>
          <h1 className="text-[24px] font-bold leading-tight">
            {!local && !servidor ? 'Pedido não encontrado neste aparelho' : recebidoServidor ? 'Pedido recebido pelo servidor' : 'Seu pedido está salvo neste aparelho.'}
          </h1>
          <p className={`mt-1 text-[15px] ${recebidoServidor ? 'text-areia/90' : ''}`}>
            {servidor ? servidor.statusDescricao : local ? (local.estado === 'salvo_no_aparelho' ? 'Ainda não transmitido ao servidor.' : descricaoEstado[local.estado]) + (online ? '' : ' Sem internet: enviaremos assim que a conexão voltar.') : ''}
          </p>
          <p className={`mt-2 text-[14px] font-semibold ${recebidoServidor ? 'text-branco' : ''}`} data-testid="nivel-comprovado">
            Último nível comprovado: {servidor?.nivelComprovado ? `Nível ${servidor.nivelComprovado} — ${servidor.nivelDescricao}` : 'Nível 1 — salvo neste aparelho'}
          </p>
          {servidor?.nivelComprovado === 5 && <p className="text-[12.5px] text-areia/80">Central SIMULADA de demonstração. Nenhum órgão real foi acionado.</p>}
          {local?.ultimoErro && !recebidoServidor && <p className="mt-1 text-[13.5px]">Última tentativa: {local.ultimoErro} ({local.tentativas}x)</p>}
          {!recebidoServidor && local && (
            <div className="mt-3 flex flex-wrap gap-2">
              <Botao variante="primario" onClick={() => transmitir(id)}>Tentar enviar agora</Botao>
              <Botao variante="secundario" onClick={() => setProximo(!proximo)} aria-expanded={proximo}>Procurar aparelhos próximos para transmitir</Botao>
            </div>
          )}
          {servidor && <p className="mt-2 text-[13px] text-areia/80">Identificador do pedido: <span className="numero-tabular">{servidor.id.slice(0, 8)}</span>{servidor.municipio ? `, ${servidor.municipio.nome}` : ''}</p>}
        </section>

        {proximo && !recebidoServidor && local && (
          <Cartao titulo="Transmitir por um aparelho próximo">
            <TransmitirProximo ocorrenciaId={id} aoExibir={marcarExibido} />
          </Cartao>
        )}

        <Cartao titulo="Etapas de entrega">
          <EtapasEntrega salvoAqui={!!local} codigoExibido={codigoExibido} canal={servidor?.canal ?? null} nivel={servidor?.nivelComprovado ?? null} />
        </Cartao>

        <Cartao titulo="Ligue também">
          <p className="mb-2 text-[14.5px]">Se houver risco imediato, ligue agora. O envio digital não substitui a ligação.</p>
          <ContatosEmergencia compacto destaque={['193', '199', '192', '190']} />
        </Cartao>

        <Complemento id={id} />
        <CompartilharComunidade id={id} atual={servidor?.compartilhamento ?? null} />
        {recebidoServidor && <ResidenciaDoPedido id={id} />}
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
          {servidor?.localizacao && <p className="mt-1 text-[13px] text-tinta-suave">Registrada no pedido: {servidor.localizacao.latitude.toFixed(5)}, {servidor.localizacao.longitude.toFixed(5)}, ±{servidor.localizacao.precisaoMetros ?? '?'} m ({formatarHora(servidor.localizacao.lidaEm)})</p>}
          {(() => {
            const l = servidor?.localizacao
            const pos = l ? { latitude: l.latitude, longitude: l.longitude, precisao: l.precisaoMetros, lidaEm: l.lidaEm } : posicao
            if (!pos) return null
            const a = servidor?.areaPublica
            return (
              <>
                <Mapa camadas={[]} posicao={pos} zoomPosicao={15} altura="h-[220px]" mostrarLegenda={false}
                  areaPublica={a ? { latitude: a.centroLatitude, longitude: a.centroLongitude, raioMetros: a.raioMetros } : null} classeExterna="mt-2" />
                <p className="mt-1 text-[12.5px] text-tinta-suave">Ponto azul: {l ? 'local registrado no pedido' : 'sua posição agora'}. Círculo azul claro: margem de precisão do GPS.{a ? ' Linha tracejada vermelha: área aproximada que a comunidade vê.' : ''}</p>
              </>
            )
          })()}
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
  const [necessidades, setNecessidades] = useState<string[]>([])
  const [enviado, setEnviado] = useState<string | null>(null)

  const enviar = async () => {
    const num = (v: string) => (v === '' ? undefined : Number(v))
    const corpo: Record<string, unknown> = { ...Object.fromEntries(Object.entries(marcados).filter(([, v]) => v)), descricao: descricao || undefined, referenciaLocal: referencia || undefined, necessidades: necessidades.length ? necessidades : undefined }
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
      <fieldset className="mt-4">
        <legend className="mb-2 text-[14px] font-medium">Do que vocês precisam?</legend>
        <div className="flex flex-wrap gap-2">
          {NECESSIDADES.map((n) => {
            const ativo = necessidades.includes(n.codigo)
            return (
              <button key={n.codigo} type="button" aria-pressed={ativo} onClick={() => setNecessidades(ativo ? necessidades.filter((x) => x !== n.codigo) : [...necessidades, n.codigo])}
                className={`min-h-11 rounded-full px-4 text-[14.5px] font-medium ring-1 ${ativo ? 'bg-seguro text-branco ring-seguro' : 'bg-branco text-tinta ring-linha'}`}>{n.rotulo}</button>
            )
          })}
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

/** O solicitante escolhe o que a comunidade vê. Nada é publicado sem esta autorização. */
function CompartilharComunidade({ id, atual }: { id: string; atual: Compartilhamento | null }) {
  const [c, setC] = useState<Compartilhamento>({ autorizado: false, incluirPessoas: true, incluirNecessidades: true })
  const [salvo, setSalvo] = useState<string | null>(null)
  useEffect(() => { if (atual) setC({ autorizado: atual.autorizado, incluirPessoas: atual.autorizado ? atual.incluirPessoas : true, incluirNecessidades: atual.autorizado ? atual.incluirNecessidades : true }) }, [atual?.autorizado, atual?.incluirPessoas, atual?.incluirNecessidades]) // eslint-disable-line
  const salvar = async (novo: Compartilhamento) => {
    setC(novo)
    const corpo = novo.autorizado ? novo : { autorizado: false, incluirPessoas: false, incluirNecessidades: false }
    await enfileirar({ id: uuid(), tipo: 'compartilhamento', caminho: `/api/ocorrencias/${id}/compartilhamento`, metodo: 'PUT', corpo, referenciaId: id })
    setSalvo(novo.autorizado ? 'Autorização salva. Ela vale assim que chegar ao servidor.' : 'Seu pedido não aparece para a comunidade.')
  }
  const caixa = (chave: 'incluirPessoas' | 'incluirNecessidades', texto: string) => (
    <label className="flex items-start gap-2 text-[14.5px]"><input type="checkbox" className="mt-1 size-5 accent-oceano" disabled={!c.autorizado} checked={c[chave]} onChange={(e) => void salvar({ ...c, [chave]: e.target.checked })} />{texto}</label>
  )
  return (
    <Cartao titulo="Pedir apoio da comunidade">
      <p className="text-[14.5px]">Vizinhos e voluntários podem oferecer água, alimentos ou transporte assistido, sempre por meio da equipe responsável. Isso não substitui o resgate.</p>
      <label className="mt-3 flex items-start gap-2 text-[15px] font-semibold"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={c.autorizado} onChange={(e) => void salvar({ ...c, autorizado: e.target.checked })} />Mostrar um resumo do meu pedido para a comunidade</label>
      <div className="mt-2 grid gap-2 pl-7">
        {caixa('incluirNecessidades', 'Incluir as necessidades (água, alimentos, abrigo…)')}
        {caixa('incluirPessoas', 'Incluir número de pessoas e faixas de crianças, idosos e mobilidade reduzida')}
      </div>
      <p className="mt-3 rounded-controle bg-fundo p-3 text-[13.5px] text-tinta-suave">A comunidade vê só o tipo de emergência, a área aproximada (um círculo de pelo menos 400 m que contém o seu local, sem marcar o ponto exato) e o que você marcou acima. Nunca: nome, telefone, relato escrito, dados de saúde ou posição exata. Você pode desligar quando quiser.</p>
      {salvo && <p role="status" className="mt-2 text-[14px] text-seguro">{salvo}</p>}
    </Cartao>
  )
}

/** Pessoa cadastrada: o pedido pode avisar familiares vinculados à residência onde ela mora. Nunca por proximidade de GPS. */
function ResidenciaDoPedido({ id }: { id: string }) {
  const { usuario } = useSessao()
  const [r, setR] = useState<{ associada: boolean; apelido: string | null; familiaresVinculados: number } | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const carregar = useCallback(() => { api<{ associada: boolean; apelido: string | null; familiaresVinculados: number }>(`/api/ocorrencias/${id}/residencia`).then(setR).catch(() => setR(null)) }, [id])
  useEffect(() => { if (usuario) carregar() }, [usuario, carregar])
  if (!usuario) return null
  const mudar = async (associar: boolean) => {
    setErro(null)
    try { await api(`/api/ocorrencias/${id}/residencia`, { method: 'POST', json: { associar } }); carregar() } catch (e) { setErro(e instanceof Error ? e.message : 'Falha') }
  }
  return (
    <Cartao titulo="Familiares vinculados">
      {r?.associada ? (
        <>
          <p className="text-[15px]">Pedido associado à sua residência <strong>"{r.apelido}"</strong>. {r.familiaresVinculados > 0 ? `${r.familiaresVinculados} familiar(es) aprovado(s) por você recebem aviso e acompanham o estado.` : 'Nenhum familiar aprovado ainda.'}</p>
          <Botao className="mt-2" variante="fantasma" onClick={() => mudar(false)}>Não avisar familiares sobre este pedido</Botao>
        </>
      ) : (
        <>
          <p className="text-[14.5px]">Este pedido não avisa familiares. Para avisar quem você aprovou, é preciso ter a residência onde mora cadastrada e a autorização "Notificação de familiares" ativa.</p>
          <div className="mt-2 flex flex-wrap gap-2">
            <Botao variante="secundario" onClick={() => mudar(true)}>Avisar meus familiares vinculados</Botao>
            <Link to="/perfil" className="inline-flex min-h-11 items-center text-[14.5px] font-semibold text-oceano underline">Configurar no perfil</Link>
          </div>
        </>
      )}
      {erro && <p role="alert" className="mt-2 text-[14px] text-emergencia-escura">{erro}</p>}
    </Cartao>
  )
}
