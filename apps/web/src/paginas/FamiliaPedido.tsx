import { Link, useParams } from 'react-router'
import { AvisoSimulado, Cartao, Selo, Vazio } from '../componentes/Base'
import { ContatosEmergencia } from '../componentes/Contatos'
import { formatarHora } from '../lib/api'
import { useDados } from '../lib/hooks'
import { useEvento } from '../lib/tempoReal'

interface VisaoFamiliar {
  id: string; referencia: string; residencia: string; municipio: string | null; criadoEm: string; recebidoPeloServidorEm: string
  estado: string; nivelComprovado: number; situacao: string[]; necessidades: string[] | null; pessoasNoLocal: number | null
  moradoresCadastrados: { total: number | null; criancas: number | null; idosos: number | null; mobilidadeReduzida: number | null } | null
  posicao: { latitude: number; longitude: number; precisaoMetros: number | null; lidaEm: string | null } | null
  historico: { em: string; tipo: string }[]
  permissoes: { verNecessidades: boolean; verMoradores: boolean; verPosicaoDoPedido: boolean }
  aviso: string
}

const EVENTO: Record<string, string> = {
  registro: 'Pedido registrado', encaminhamento: 'Encaminhamento à central simulada', retorno_central: 'Retorno da central simulada',
  complemento: 'Informações complementares', status: 'Mudança de estado', residencia: 'Associado à residência', situacao: 'Situação atualizada',
}

/** Pedido visto por familiar com vínculo aprovado: só o que o morador permitiu. */
export function FamiliaPedido() {
  const { id } = useParams()
  const { dados: d, erro, recarregar } = useDados<VisaoFamiliar>(`/api/eu/familia/pedidos/${id}`, { intervaloMs: 30000 })
  useEvento('notificacao', () => { void recarregar() })
  if (erro) return <Vazio>{erro}</Vazio>
  if (!d) return <p>Carregando…</p>
  return (
    <div className="mx-auto grid max-w-2xl gap-4">
      <Link to="/perfil" className="text-[15px] font-semibold text-oceano underline">Voltar</Link>
      <section className="rounded-cartao bg-emergencia p-5 text-branco">
        <h1 className="text-[23px] font-bold leading-tight">Pedido de socorro na residência "{d.residencia}"</h1>
        <p className="mt-1 text-[15px]">{d.municipio ?? 'Município não identificado'} · feito às {formatarHora(d.criadoEm)} · Ref. {d.referencia}</p>
      </section>
      <Cartao titulo="Estado de recebimento">
        <p className="text-[17px] font-semibold">{d.estado}</p>
        <p className="mt-1 text-[14px]">Nível {d.nivelComprovado}: {d.nivelComprovado === 5 ? 'a equipe de teste (simulada) recebeu' : 'pedido de ajuda registrado'} ({formatarHora(d.recebidoPeloServidorEm)}).</p>
        <ul className="mt-3 grid gap-1 text-[13.5px]">
          {d.historico.map((h, i) => <li key={i}>{formatarHora(h.em)} · {EVENTO[h.tipo] ?? h.tipo}</li>)}
        </ul>
      </Cartao>
      <Cartao titulo="Informações compartilhadas com você">
        <p className="text-[15px]">{d.situacao.join(' · ')}</p>
        {d.permissoes.verNecessidades ? <p className="mt-1 text-[15px]">Precisa de: {d.necessidades && d.necessidades.length ? d.necessidades.join(', ') : 'não informado'}</p> : <p className="mt-1 text-[13.5px] text-tinta-suave">Necessidades: o morador não compartilhou.</p>}
        {d.permissoes.verMoradores && <p className="mt-1 text-[15px]">Pessoas no local: {d.pessoasNoLocal ?? 'não informado'}{d.moradoresCadastrados?.total != null ? ` · moradores cadastrados: ${d.moradoresCadastrados.total}` : ''}</p>}
        {d.posicao ? <p className="mt-1 text-[15px]">Posição do pedido: {d.posicao.latitude.toFixed(5)}, {d.posicao.longitude.toFixed(5)} (±{d.posicao.precisaoMetros ?? '?'} m)</p> : <p className="mt-1 text-[13.5px] text-tinta-suave">Posição exata: o morador não compartilhou.</p>}
        <div className="mt-2"><Selo tom="neutro">Permissões definidas pelo morador</Selo></div>
      </Cartao>
      <Cartao titulo="Se houver risco imediato"><ContatosEmergencia compacto destaque={['193', '199', '192']} /></Cartao>
      <AvisoSimulado texto={d.aviso} />
    </div>
  )
}
