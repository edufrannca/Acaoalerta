import { useState } from 'react'
import { Link } from 'react-router'
import { AvisoSimulado, Botao, Campo, Cartao, Selo, classeEntrada } from '../componentes/Base'
import { api, formatarHora } from '../lib/api'
import { useDados } from '../lib/hooks'
import { MUNICIPIOS, nomeMunicipio } from '../lib/municipios'
import { useSessao } from '../lib/sessao'

interface Central { codigo: string; nome: string; tipo: string; municipioCodigo: string | null; modo: string; atrasoMs: number; recebimentos: number }
interface Estado { ia: { configurada: boolean; descricao: string; motorRegras: string }; push: { configurado: boolean }; fontes: { nome: string; disponivel: boolean; ultimoSucessoEm: string | null; ultimoErro: string | null; registros: number | null }[] }

const MODOS = [
  { modo: 'Normal', rotulo: 'Normal' }, { modo: 'Indisponivel', rotulo: 'Indisponível (503)' }, { modo: 'Atraso', rotulo: 'Atraso de 3 s' },
  { modo: 'Rejeitar', rotulo: 'Rejeitar (422)' }, { modo: 'ErroAutenticacao', rotulo: 'Erro de autenticação (401)' }, { modo: 'Intermitente', rotulo: 'Intermitente (50%)' }, { modo: 'Timeout', rotulo: 'Sem resposta (timeout)' },
]

/** Polígonos fictícios usados nos cenários de alerta proativo (cobrem os locais monitorados das contas demo). */
const AREAS_DEMO: Record<string, { rotulo: string; geo: string }> = {
  ibura: { rotulo: 'Ibura e entorno (Recife)', geo: '{"type":"Polygon","coordinates":[[[-34.97,-8.15],[-34.92,-8.15],[-34.92,-8.10],[-34.97,-8.10],[-34.97,-8.15]]]}' },
  olinda: { rotulo: 'Sítio Histórico (Olinda)', geo: '{"type":"Polygon","coordinates":[[[-34.87,-8.025],[-34.845,-8.025],[-34.845,-7.995],[-34.87,-7.995],[-34.87,-8.025]]]}' },
}

const ROTEIRO = [
  'Prevenção: entre como Ana (cidadã), veja os riscos conhecidos do seu ponto e as fontes.',
  'Alerta proativo: aqui, publique um aviso demonstrativo para "Ibura e entorno".',
  'Check-in: como Ana, responda "Você e sua família estão bem?" com Estou seguro.',
  'Pedido de socorro: em outro aparelho (ou aba anônima), toque em Preciso de socorro e marque ilhado + água subindo.',
  'Análise: no painel da Defesa Civil Recife, abra a ocorrência e mostre sinalizações e evidências.',
  'Encaminhamento: mostre os protocolos de DC-RECIFE e CBM-SIM e abra a central receptora.',
  'Falha institucional: deixe DC-RECIFE indisponível, faça novo pedido, mostre a falha e a retentativa ao reativar.',
  'Offline: desligue a internet, faça um pedido, mostre "salvo neste aparelho", religue e veja a sincronização.',
  'Apoio: como Bia, registre uma oferta e participe; como organização, aceite na central.',
  'Abrigos: no mapa, diferencie abrigos da fonte pública e fictícios, todos com disponibilidade não verificada.',
]

export function Demonstracao() {
  const { usuario } = useSessao()
  const admin = usuario?.papel === 'Administrador'
  const centrais = useDados<{ centrais: Central[] }>(admin ? '/simulador/centrais' : null, { intervaloMs: 10000 })
  const estado = useDados<Estado>('/api/estado', { intervaloMs: 30000 })
  const [titulo, setTitulo] = useState('Chuva intensa com risco de alagamentos')
  const [area, setArea] = useState<'ibura' | 'olinda' | 'municipio'>('ibura')
  const [municipio, setMunicipio] = useState('2611606')
  const [duracao, setDuracao] = useState(60)
  const [msg, setMsg] = useState<string | null>(null)

  if (!admin) return <Cartao titulo="Controle da demonstração"><p>Entre como <Link to="/perfil" className="text-oceano underline">Administrador da demonstração</Link>.</p></Cartao>

  const publicar = async () => {
    try {
      const corpo = { titulo, severidade: 'Perigo', duracaoMinutos: duracao, municipioCodigo: area === 'municipio' ? municipio : null, poligonoGeoJson: area === 'municipio' ? null : AREAS_DEMO[area].geo, instrucoes: null }
      await api('/api/demo/avisos', { method: 'POST', json: corpo })
      const r = await api<{ notificacoes: number }>('/api/demo/avisos/processar', { method: 'POST' })
      setMsg(`Aviso DEMONSTRATIVO publicado. ${r.notificacoes} usuário(s) com local monitorado na área foram notificados.`)
    } catch (e) { setMsg(e instanceof Error ? e.message : 'Falha') }
  }

  const mudarModo = async (c: string, modo: string) => {
    await api(`/simulador/centrais/${c}/modo`, { method: 'PUT', json: { modo, atrasoMs: modo === 'Atraso' ? 3000 : 0 } })
    void centrais.recarregar()
  }

  return (
    <div className="grid gap-4">
      <h1 className="text-[26px] font-bold text-petroleo">Controle da demonstração</h1>
      <AvisoSimulado texto="Avisos criados aqui são DEMONSTRATIVOS e nunca são apresentados como oficiais." />
      <div className="grid gap-4 lg:grid-cols-2">
        <Cartao titulo="Publicar aviso demonstrativo">
          <div className="grid gap-3">
            <Campo rotulo="Título"><input className={classeEntrada} value={titulo} onChange={(e) => setTitulo(e.target.value)} /></Campo>
            <Campo rotulo="Área">
              <select className={classeEntrada} value={area} onChange={(e) => setArea(e.target.value as typeof area)}>
                {Object.entries(AREAS_DEMO).map(([k, v]) => <option key={k} value={k}>{v.rotulo}</option>)}
                <option value="municipio">Município inteiro</option>
              </select>
            </Campo>
            {area === 'municipio' && <Campo rotulo="Município"><select className={classeEntrada} value={municipio} onChange={(e) => setMunicipio(e.target.value)}>{Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}</select></Campo>}
            <Campo rotulo="Duração (minutos)"><input type="number" min={5} max={1440} className={classeEntrada} value={duracao} onChange={(e) => setDuracao(Number(e.target.value))} /></Campo>
            <Botao onClick={publicar}>Publicar e processar alerta proativo</Botao>
            {msg && <p role="status" className="text-[14px] text-seguro">{msg}</p>}
          </div>
        </Cartao>
        <Cartao titulo="Estado do ambiente">
          <ul className="grid gap-1.5 text-[14px]">
            <li>IA: <Selo tom={estado.dados?.ia.configurada ? 'ok' : 'neutro'}>{estado.dados?.ia.descricao ?? '…'}</Selo></li>
            <li>Motor de regras: {estado.dados?.ia.motorRegras}</li>
            <li>Web Push: <Selo tom={estado.dados?.push.configurado ? 'ok' : 'neutro'}>{estado.dados?.push.configurado ? 'configurado' : 'não configurado'}</Selo></li>
          </ul>
          <p className="mt-3 font-semibold text-petroleo">Fontes públicas</p>
          <ul className="mt-1 grid gap-1.5 text-[13.5px]">
            {(estado.dados?.fontes ?? []).map((f) => (
              <li key={f.nome}><Selo tom={f.disponivel ? 'ok' : 'erro'}>{f.disponivel ? 'disponível' : 'indisponível'}</Selo> {f.nome}{f.registros != null ? `, ${f.registros} registros` : ''}{f.ultimoSucessoEm ? `, último sucesso ${formatarHora(f.ultimoSucessoEm)}` : ''}{f.ultimoErro ? `. ${f.ultimoErro}` : ''}</li>
            ))}
          </ul>
        </Cartao>
      </div>

      <Cartao titulo="Centrais simuladas e falhas controladas">
        <div className="overflow-x-auto">
          <table className="w-full min-w-[640px] text-left text-[14px]">
            <thead><tr className="border-b border-linha text-tinta-suave"><th className="py-2 font-medium">Central</th><th className="font-medium">Jurisdição</th><th className="font-medium">Recebidas</th><th className="font-medium">Modo</th></tr></thead>
            <tbody>
              {(centrais.dados?.centrais ?? []).map((c) => (
                <tr key={c.codigo} className="border-b border-linha/60">
                  <td className="py-2"><Link to={`/central/${c.codigo}`} className="font-medium text-oceano underline-offset-2 hover:underline">{c.codigo}</Link><span className="block text-[12.5px] text-tinta-suave">{c.nome}</span></td>
                  <td>{c.municipioCodigo ? nomeMunicipio(c.municipioCodigo) : 'RMR'}</td>
                  <td className="numero-tabular">{c.recebimentos}</td>
                  <td>
                    <select aria-label={`Modo da central ${c.codigo}`} className={`${classeEntrada} py-1.5`} value={c.modo} onChange={(e) => mudarModo(c.codigo, e.target.value)}>
                      {MODOS.map((m) => <option key={m.modo} value={m.modo}>{m.rotulo}</option>)}
                    </select>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </Cartao>

      <ChaveGateway />

      <Cartao titulo="Roteiro da apresentação">
        <ol className="grid list-decimal gap-1.5 pl-5 text-[14.5px]">{ROTEIRO.map((r) => <li key={r}>{r}</li>)}</ol>
      </Cartao>
    </div>
  )
}

/** Chave para os apps nativos de proximidade atuarem como gateway. Mostrada só ao administrador, nunca embutida nos apps. */
function ChaveGateway() {
  const [receptor, setReceptor] = useState('gateway-android')
  const [chave, setChave] = useState<string | null>(null)
  const [visivel, setVisivel] = useState(false)
  const gerar = async () => {
    const r = await api<{ chave: string }>(`/api/demo/gateway-chave?receptor=${encodeURIComponent(receptor)}`)
    setChave(r.chave); setVisivel(false)
  }
  return (
    <Cartao titulo="Chave do gateway (apps nativos Nearby)">
      <p className="text-[14px] text-tinta-suave">Digite no app Android/iOS do aparelho que vai encaminhar ao servidor: URL <strong>{window.location.origin}</strong>, o identificador abaixo e a chave. Código do grupo: <strong>ALERTA-DEMO-2026</strong>.</p>
      <div className="mt-2 flex flex-wrap items-end gap-2">
        <label className="text-[14px]">Identificador do gateway
          <input className="mt-1 block min-h-11 rounded-controle border border-linha px-3" value={receptor} onChange={(e) => { setReceptor(e.target.value); setChave(null) }} />
        </label>
        <Botao variante="secundario" onClick={gerar}>Gerar chave</Botao>
      </div>
      {chave && (
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <code className="break-all rounded-controle bg-areia px-3 py-2 text-[15px]">{visivel ? chave : '••••••••••••••••'}</code>
          <Botao variante="secundario" onClick={() => setVisivel(!visivel)}>{visivel ? 'Ocultar' : 'Mostrar'}</Botao>
          <Botao variante="secundario" onClick={() => navigator.clipboard?.writeText(chave)}>Copiar</Botao>
        </div>
      )}
    </Cartao>
  )
}
