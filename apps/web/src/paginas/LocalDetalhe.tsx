import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Botao, Cartao, Selo, Vazio } from '../componentes/Base'
import { LeitorVoz } from '../componentes/LeitorVoz'
import { CondicoesAlagamento } from '../componentes/CondicoesAlagamento'
import { Mapa } from '../componentes/Mapa'
import { CartaoPedido } from '../componentes/PedidosComunidade'
import { api, formatarHora } from '../lib/api'
import { usePedidosComunitarios } from '../lib/comunidade'
import { useDados } from '../lib/hooks'
import type { Contexto } from '../lib/regiao'
import { useEvento } from '../lib/tempoReal'
import { ROTULO_ORIGEM, ROTULO_TIPO, type LocalMonitorado, type Permissoes } from '../lib/residencias'
import { ClimaRisco } from './Inicio'

interface Vinculos { convitesEmAberto: number; vinculos: { id: string; familiar: string | null; status: string; permissoes: Permissoes; criadoEm: string; decididoEm: string | null }[] }

/** Tela do local monitorado: contexto do LOCAL (não da posição de quem consulta), atualizado enquanto o app está aberto. */
export function LocalDetalhe() {
  const { id } = useParams()
  const navegar = useNavigate()
  const local = useDados<LocalMonitorado>(`/api/eu/locais/${id}`)
  const l = local.dados
  const ctx = useDados<Contexto>(l ? `/api/territorio/contexto?lat=${l.latitude}&lon=${l.longitude}` : null, { cache: true, intervaloMs: 5 * 60_000 })
  const pos = l ? { latitude: l.latitude, longitude: l.longitude, precisao: null, lidaEm: l.atualizadoEm } : null
  const comunidade = usePedidosComunitarios(pos, 2)
  // Aviso novo publicado (SignalR): atualiza na hora.
  useEvento('avisoPublicado', () => { void ctx.recarregar() })

  if (local.erro) return <Vazio>{local.erro}</Vazio>
  if (!l) return <p>Carregando…</p>
  const remover = async () => {
    if (!confirm(`Remover "${l.apelido}"? ${l.moroAqui ? 'Os vínculos com familiares desta residência também serão encerrados.' : ''}`)) return
    await api(`/api/eu/locais/${l.id}`, { method: 'DELETE' })
    navegar('/perfil')
  }
  const resumo = ctx.dados ? `${l.apelido}, ${l.municipio}. ${ctx.dados.estados.map((e) => `${e.titulo}: ${e.detalhe}`).join(' ')}` : ''

  return (
    <div className="grid gap-4">
      <Link to="/perfil" className="text-[15px] font-semibold text-oceano underline">Voltar ao perfil</Link>
      <div className="flex flex-wrap items-end justify-between gap-2">
        <div>
          <h1 className="text-[26px] font-bold text-petroleo">{l.apelido}</h1>
          <p className="text-[14.5px] text-tinta-suave">{ROTULO_TIPO[l.tipo]}{l.moroAqui ? ' · onde você mora' : ''} · {l.municipio ?? 'fora da RMR'} · posição: {ROTULO_ORIGEM[l.origem]}{l.precisao ? ` (${l.precisao})` : ''}</p>
        </div>
        <div className="flex gap-2">
          <Link to={`/locais/${l.id}/editar`} className="inline-flex min-h-11 items-center rounded-controle bg-branco px-4 text-[15px] font-semibold text-petroleo ring-1 ring-linha">Editar</Link>
          <Botao variante="fantasma" onClick={remover}>Remover</Botao>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-[1.4fr_1fr]">
        <div className="grid content-start gap-4">
          <Mapa camadas={['limites', 'avisos', 'chuva', 'areasRisco', 'pedidos']} posicao={pos} zoomPosicao={14} mostrarPrecisao={false} pedidos={comunidade.dados?.pedidos} altura="h-[360px]" />
          <p className="text-[12.5px] text-tinta-suave">Ponto azul: o local monitorado. Informações calculadas para este ponto, não para onde você está agora.</p>
        </div>
        <div className="grid content-start gap-4">
          <CondicoesAlagamento latitude={l.latitude} longitude={l.longitude} regiao={`${l.apelido}${l.municipio ? `, ${l.municipio}` : ''}`} titulo="Chuva e atenção a alagamentos neste local" />
          <ClimaRisco ctx={ctx.dados} doCache={ctx.doCache} temPosicao />
          {ctx.dados && <p className="text-[12.5px] text-tinta-suave">Atualizado às {formatarHora(ctx.dados.avaliadoEm)}; nova consulta a cada 5 minutos com o app aberto. Os dados de chuva seguem a frequência de publicação das fontes (APAC/INMET).</p>}
          {resumo && <LeitorVoz texto={resumo} rotulo="Ouvir situação deste local" />}
          {ctx.dados && ctx.dados.orientacoes.length > 0 && (
            <Cartao titulo="Orientações preventivas">
              <ul className="grid list-disc gap-1 pl-5 text-[14.5px]">{ctx.dados.orientacoes.slice(0, 5).map((o) => <li key={o}>{o}</li>)}</ul>
            </Cartao>
          )}
          <Cartao titulo="Pedidos de ajuda compartilhados a até 2 km">
            {(comunidade.dados?.pedidos ?? []).length === 0
              ? <p className="text-[14px] text-tinta-suave">Nenhum pedido compartilhado por perto nas últimas 24 horas.</p>
              : <div className="grid gap-2">{comunidade.dados!.pedidos.slice(0, 3).map((p) => <CartaoPedido key={p.id} p={p} compacto />)}<Link to="/ajudar" className="text-[14.5px] font-semibold text-oceano underline">Oferecer ajuda</Link></div>}
          </Cartao>
        </div>
      </div>

      {l.tipo === 'Residencia' && (
        <Cartao titulo="Moradores (privado)">
          {l.moradores.total == null ? <p className="text-[14px] text-tinta-suave">Não informado. <Link className="text-oceano underline" to={`/locais/${l.id}/editar`}>Informar</Link></p> : (
            <p className="text-[15px]">{l.moradores.total} morador(es) · crianças: {l.moradores.criancas ?? '–'} · idosos: {l.moradores.idosos ?? '–'} · mobilidade reduzida: {l.moradores.mobilidadeReduzida ?? '–'} · dependem de assistência: {l.moradores.dependemAssistencia ?? '–'}</p>
          )}
          <p className="mt-1 text-[12.5px] text-tinta-suave">Cifrado. Não aparece para a comunidade. Familiares só veem se você permitir no vínculo.</p>
        </Cartao>
      )}

      {l.tipo === 'Residencia' && l.moroAqui ? <VinculosResidencia localId={l.id} /> : l.tipo === 'Residencia' && (
        <Cartao titulo="Familiares">
          <p className="text-[14px] text-tinta-suave">Para convidar familiares a acompanhar pedidos de socorro desta residência, marque "Eu moro aqui" na edição do local.</p>
        </Cartao>
      )}
    </div>
  )
}

const ROTULO_STATUS: Record<string, { t: string; tom: 'ok' | 'espera' | 'neutro' | 'erro' }> = {
  AguardandoAprovacao: { t: 'Aguardando sua aprovação', tom: 'espera' }, Ativo: { t: 'Ativo', tom: 'ok' }, Revogado: { t: 'Encerrado', tom: 'neutro' }, Recusado: { t: 'Recusado', tom: 'neutro' },
}

function VinculosResidencia({ localId }: { localId: string }) {
  const { dados, recarregar } = useDados<Vinculos>(`/api/eu/locais/${localId}/vinculos`, { intervaloMs: 30000 })
  const [convite, setConvite] = useState<{ codigo: string; expiraEm: string } | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const agir = async (f: () => Promise<unknown>) => { setErro(null); try { await f(); void recarregar() } catch (e) { setErro(e instanceof Error ? e.message : 'Falha') } }
  const permissao = (k: keyof Permissoes, rotulo: string, v: Vinculos['vinculos'][number]) => (
    <label className="flex items-start gap-2 text-[14px]"><input type="checkbox" className="mt-0.5 size-5 accent-oceano" checked={v.permissoes[k]}
      onChange={(e) => agir(() => api(`/api/eu/vinculos/${v.id}/permissoes`, { method: 'PUT', json: { ...v.permissoes, [k]: e.target.checked } }))} />{rotulo}</label>
  )
  return (
    <Cartao titulo="Familiares que acompanham esta residência">
      <p className="text-[14px]">Quem você aprovar recebe aviso quando você pedir socorro (se a autorização "Notificação de familiares" estiver ativa no seu perfil). Endereço, CEP ou estar perto não criam vínculo.</p>
      <div className="mt-3 flex flex-wrap items-center gap-2">
        <Botao variante="secundario" onClick={() => agir(async () => setConvite(await api(`/api/eu/locais/${localId}/convites`, { method: 'POST' })))}>Gerar código de convite</Botao>
        {dados && dados.convitesEmAberto > 0 && <span className="text-[13px] text-tinta-suave">{dados.convitesEmAberto} convite(s) em aberto</span>}
      </div>
      {convite && (
        <div role="status" className="mt-3 rounded-controle bg-oceano-suave p-3">
          <p className="text-[13.5px]">Envie este código ao familiar. Vale até {formatarHora(convite.expiraEm)} e só pode ser usado uma vez. Ele não aparece de novo.</p>
          <p className="numero-tabular mt-1 select-all text-[26px] font-bold tracking-widest text-petroleo">{convite.codigo}</p>
          <p className="text-[13px]">O familiar digita em Perfil → Acompanhar uma residência. Depois você aprova aqui.</p>
        </div>
      )}
      {erro && <p role="alert" className="mt-2 text-[14px] text-emergencia-escura">{erro}</p>}
      <ul className="mt-3 grid gap-2">
        {(dados?.vinculos ?? []).filter((v) => v.status !== 'Recusado').map((v) => (
          <li key={v.id} className="rounded-controle bg-areia p-3">
            <div className="flex flex-wrap items-center justify-between gap-2"><span className="font-semibold">{v.familiar ?? 'Familiar'}</span><Selo tom={ROTULO_STATUS[v.status]?.tom ?? 'neutro'}>{ROTULO_STATUS[v.status]?.t ?? v.status}</Selo></div>
            {v.status === 'AguardandoAprovacao' && (
              <div className="mt-2 flex flex-wrap gap-2">
                <Botao onClick={() => agir(() => api(`/api/eu/vinculos/${v.id}/decisao`, { method: 'POST', json: { aprovar: true } }))}>Aprovar</Botao>
                <Botao variante="fantasma" onClick={() => agir(() => api(`/api/eu/vinculos/${v.id}/decisao`, { method: 'POST', json: { aprovar: false } }))}>Recusar</Botao>
              </div>
            )}
            {v.status === 'Ativo' && (
              <div className="mt-2 grid gap-1.5">
                {permissao('receberNotificacoes', 'Receber avisos dos meus pedidos de socorro', v)}
                {permissao('verNecessidades', 'Ver as necessidades informadas', v)}
                {permissao('verMoradores', 'Ver número de pessoas e moradores', v)}
                {permissao('verPosicaoDoPedido', 'Ver a posição exata do pedido', v)}
                <Botao variante="fantasma" className="mt-1 justify-self-start" onClick={() => agir(() => api(`/api/eu/vinculos/${v.id}`, { method: 'DELETE' }))}>Remover acesso</Botao>
              </div>
            )}
          </li>
        ))}
      </ul>
    </Cartao>
  )
}
