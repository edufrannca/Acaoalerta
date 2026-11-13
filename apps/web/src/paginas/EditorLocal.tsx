import { useEffect, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router'
import { Botao, Campo, Cartao, classeEntrada } from '../componentes/Base'
import { SeletorMapa, type Ponto } from '../componentes/SeletorMapa'
import { api } from '../lib/api'
import { useLocalizacao } from '../lib/hooks'
import { MUNICIPIOS } from '../lib/municipios'
import { ROTULO_TIPO, type Endereco, type LocalMonitorado, type Moradores, type OrigemPosicao, type ResultadoGeo, type TipoLocal } from '../lib/residencias'

type Metodo = 'cep' | 'endereco' | 'mapa' | 'gps'
const METODOS: { id: Metodo; rotulo: string }[] = [
  { id: 'cep', rotulo: 'Pelo CEP' }, { id: 'endereco', rotulo: 'Pelo endereço' }, { id: 'mapa', rotulo: 'No mapa' }, { id: 'gps', rotulo: 'Onde estou' },
]
const enderecoVazio: Endereco = { cep: null, logradouro: null, numero: null, complemento: null, bairro: null, municipio: null, uf: 'PE' }
const moradoresVazio: Moradores = { total: null, criancas: null, idosos: null, mobilidadeReduzida: null, dependemAssistencia: null, contatoReferencia: null, observacoes: null }

interface RespostaCep { endereco: { cep: string; logradouro: string | null; bairro: string | null; municipio: string; uf: string }; dentroRmr: boolean; posicoes: ResultadoGeo[]; exigeEscolhaNoMapa: boolean; aviso: string | null }
interface RespostaEndereco { resultados: (ResultadoGeo & { municipio: { nome: string } | null })[]; ambiguo: boolean; aviso: string | null }

const numero = (v: string) => (v === '' ? null : Math.max(0, Math.min(100, Number(v))))

export function EditorLocal() {
  const { id } = useParams()
  const navegar = useNavigate()
  const [metodo, setMetodo] = useState<Metodo>('cep')
  const [apelido, setApelido] = useState('')
  const [tipo, setTipo] = useState<TipoLocal>('Residencia')
  const [moroAqui, setMoroAqui] = useState(false)
  const [end, setEnd] = useState<Endereco>(enderecoVazio)
  const [mor, setMor] = useState<Moradores>(moradoresVazio)
  const [ponto, setPonto] = useState<Ponto | null>(null)
  const [origem, setOrigem] = useState<OrigemPosicao>('EscolhidoNoMapa')
  const [precisao, setPrecisao] = useState<string | null>(null)
  const [municipioPonto, setMunicipioPonto] = useState<string | null>(null)
  const [confirmado, setConfirmado] = useState(false)
  const [resultados, setResultados] = useState<ResultadoGeo[]>([])
  const [aviso, setAviso] = useState<string | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [ocupado, setOcupado] = useState(false)
  const { posicao, estado: gps, iniciar } = useLocalizacao(10000)

  useEffect(() => {
    if (!id) return
    api<LocalMonitorado>(`/api/eu/locais/${id}`).then((l) => {
      setApelido(l.apelido); setTipo(l.tipo); setMoroAqui(l.moroAqui); setEnd({ ...enderecoVazio, ...l.endereco }); setMor({ ...moradoresVazio, ...l.moradores })
      setPonto({ latitude: l.latitude, longitude: l.longitude }); setOrigem(l.origem); setPrecisao(l.precisao); setMetodo('mapa'); setConfirmado(true)
    }).catch((e) => setErro(e instanceof Error ? e.message : 'Local não encontrado'))
  }, [id])

  // Município pelo limite oficial sempre que o ponto muda (sem serviço externo).
  useEffect(() => {
    if (!ponto) { setMunicipioPonto(null); return }
    const t = setTimeout(() => {
      api<{ municipio: { nome: string } | null }>(`/api/geo/ponto?lat=${ponto.latitude}&lon=${ponto.longitude}`)
        .then((r) => setMunicipioPonto(r.municipio?.nome ?? 'fora da RMR')).catch(() => setMunicipioPonto(null))
    }, 300)
    return () => clearTimeout(t)
  }, [ponto?.latitude, ponto?.longitude]) // eslint-disable-line react-hooks/exhaustive-deps

  const escolher = (p: Ponto, o: OrigemPosicao, desc: string | null) => { setPonto(p); setOrigem(o); setPrecisao(desc); setConfirmado(false) }
  const tocarMapa = (p: Ponto) => escolher(p, 'EscolhidoNoMapa', origem === 'EscolhidoNoMapa' ? 'Escolhido no mapa' : 'Ajustado no mapa')

  const executar = async (f: () => Promise<void>) => { setErro(null); setOcupado(true); try { await f() } catch (e) { setErro(e instanceof Error ? e.message : 'Falha') } finally { setOcupado(false) } }

  const buscarCep = () => executar(async () => {
    setResultados([]); setAviso(null)
    const r = await api<RespostaCep>(`/api/geo/cep/${(end.cep ?? '').replace(/\D/g, '')}`)
    setEnd({ ...end, cep: r.endereco.cep, logradouro: r.endereco.logradouro, bairro: r.endereco.bairro, municipio: r.endereco.municipio, uf: r.endereco.uf })
    if (!r.dentroRmr) { setAviso('Este CEP fica fora dos 14 municípios da Região Metropolitana do Recife, que o Alerta em Ação cobre.'); return }
    setResultados(r.posicoes)
    if (r.posicoes.length === 1) escolher(r.posicoes[0], 'CepGeocodificado', r.posicoes[0].precisao)
    setAviso(r.aviso ?? (r.exigeEscolhaNoMapa ? 'Confira o marcador e ajuste no mapa se precisar.' : null))
  })

  const buscarEndereco = () => executar(async () => {
    setResultados([]); setAviso(null)
    const q = new URLSearchParams({ municipio: end.municipio ?? '', uf: 'PE' })
    if (end.logradouro) q.set('logradouro', end.logradouro)
    if (end.numero) q.set('numero', end.numero)
    if (end.bairro) q.set('bairro', end.bairro)
    if (end.cep) q.set('cep', end.cep)
    const r = await api<RespostaEndereco>(`/api/geo/endereco?${q}`)
    setResultados(r.resultados)
    if (r.resultados.length === 1) escolher(r.resultados[0], 'EnderecoGeocodificado', r.resultados[0].precisao)
    setAviso(r.aviso)
  })

  const salvar = () => executar(async () => {
    if (!ponto) throw new Error('Escolha a posição do local.')
    const corpo = { apelido, latitude: ponto.latitude, longitude: ponto.longitude, tipo, origem, precisao, moroAqui: tipo === 'Residencia' && moroAqui,
      endereco: { ...end, cep: end.cep ? end.cep.replace(/\D/g, '') || null : null }, moradores: tipo === 'Residencia' ? mor : null }
    const l = await api<LocalMonitorado>(id ? `/api/eu/locais/${id}` : '/api/eu/locais', { method: id ? 'PUT' : 'POST', json: corpo })
    navegar(`/locais/${l.id}`)
  })

  const campoEnd = (k: keyof Endereco, rotulo: string, extra?: Partial<React.InputHTMLAttributes<HTMLInputElement>>) => (
    <Campo rotulo={rotulo}><input className={classeEntrada} value={end[k] ?? ''} onChange={(e) => setEnd({ ...end, [k]: e.target.value || null })} {...extra} /></Campo>
  )
  const campoMor = (k: keyof Moradores, rotulo: string) => (
    <Campo rotulo={rotulo}><input type="number" min={0} max={100} inputMode="numeric" className={classeEntrada} value={mor[k] ?? ''} onChange={(e) => setMor({ ...mor, [k]: numero(e.target.value) })} /></Campo>
  )

  return (
    <div className="mx-auto grid max-w-3xl gap-4">
      <Link to="/perfil" className="text-[15px] font-semibold text-oceano underline">Voltar ao perfil</Link>
      <h1 className="text-[26px] font-bold text-petroleo">{id ? 'Editar local monitorado' : 'Novo local monitorado'}</h1>
      <p className="text-[14.5px] text-tinta-suave">Monitore sua casa, o trabalho ou a casa de um familiar, mesmo estando longe. Até 5 locais.</p>

      <Cartao titulo="1. Onde fica">
        <div role="tablist" aria-label="Como informar o local" className="mb-3 flex flex-wrap gap-2">
          {METODOS.map((m) => (
            <button key={m.id} role="tab" aria-selected={metodo === m.id} type="button" onClick={() => { setMetodo(m.id); setResultados([]); setAviso(null) }}
              className={`min-h-11 rounded-full px-4 text-[15px] font-semibold ring-1 ${metodo === m.id ? 'bg-petroleo text-branco ring-petroleo' : 'bg-branco text-petroleo ring-linha'}`}>{m.rotulo}</button>
          ))}
        </div>

        {metodo === 'cep' && (
          <div className="grid gap-3">
            <div className="grid grid-cols-[1fr_auto] items-end gap-2">
              {campoEnd('cep', 'CEP (8 dígitos)', { inputMode: 'numeric', maxLength: 9, placeholder: '00000-000' })}
              <Botao onClick={buscarCep} disabled={ocupado || (end.cep ?? '').replace(/\D/g, '').length !== 8}>Buscar CEP</Botao>
            </div>
            {end.municipio && (
              <div className="grid gap-3 sm:grid-cols-2">
                {campoEnd('logradouro', 'Rua ou avenida')}
                {campoEnd('numero', 'Número')}
                {campoEnd('complemento', 'Complemento')}
                {campoEnd('bairro', 'Bairro')}
                <p className="text-[14px] sm:col-span-2">{end.municipio} – {end.uf}</p>
                {end.numero && <Botao variante="secundario" onClick={buscarEndereco} disabled={ocupado}>Localizar com o número</Botao>}
              </div>
            )}
            <p className="text-[12.5px] text-tinta-suave">Endereço pelo ViaCEP, que não informa coordenadas. A posição vem do OpenStreetMap quando há correspondência; sem ela, você escolhe no mapa.</p>
          </div>
        )}

        {metodo === 'endereco' && (
          <div className="grid gap-3 sm:grid-cols-2">
            {campoEnd('logradouro', 'Rua ou avenida')}
            {campoEnd('numero', 'Número')}
            {campoEnd('bairro', 'Bairro')}
            <Campo rotulo="Município">
              <select className={classeEntrada} value={end.municipio ?? ''} onChange={(e) => setEnd({ ...end, municipio: e.target.value || null, uf: 'PE' })}>
                <option value="">Escolha</option>{Object.values(MUNICIPIOS).map((n) => <option key={n} value={n}>{n}</option>)}
              </select>
            </Campo>
            <p className="text-[14px]">Estado: Pernambuco (PE)</p>
            <Botao onClick={buscarEndereco} disabled={ocupado || !end.municipio || (!end.logradouro && !end.bairro)}>Buscar endereço</Botao>
            <p className="text-[12.5px] text-tinta-suave sm:col-span-2">A busca só acontece quando você toca em Buscar (OpenStreetMap/Nominatim, uma consulta por vez).</p>
          </div>
        )}

        {metodo === 'gps' && (
          <div className="grid gap-2">
            <Botao onClick={() => (posicao ? escolher(posicao, 'GpsAtual', `GPS do aparelho (±${posicao.precisao ?? '?'} m)`) : iniciar())}>
              {posicao ? `Usar minha posição atual (±${posicao.precisao ?? '?'} m)` : gps === 'negado' ? 'Localização negada' : 'Ativar localização'}
            </Botao>
            <p className="text-[12.5px] text-tinta-suave">Use só se você estiver no local agora. Para a casa de um familiar, prefira CEP, endereço ou mapa.</p>
          </div>
        )}

        {resultados.length > 1 && (
          <fieldset className="mt-3">
            <legend className="mb-1 text-[14px] font-semibold">Mais de um lugar corresponde. Qual é o certo?</legend>
            <div className="grid gap-2">
              {resultados.map((r, i) => (
                <button key={i} type="button" onClick={() => escolher(r, metodo === 'cep' ? 'CepGeocodificado' : 'EnderecoGeocodificado', r.precisao)}
                  className={`min-h-11 rounded-controle px-3 py-2 text-left text-[14px] ring-1 ${ponto?.latitude === r.latitude && ponto?.longitude === r.longitude ? 'bg-oceano-suave ring-oceano' : 'bg-branco ring-linha'}`}>
                  {r.descricao}<span className="block text-[12.5px] text-tinta-suave">{r.precisao}</span>
                </button>
              ))}
            </div>
          </fieldset>
        )}
        {aviso && <p role="status" className="mt-3 rounded-controle bg-atencao-claro p-3 text-[14px]">{aviso}</p>}

        <div className="mt-3">
          <SeletorMapa ponto={ponto} aoMudar={tocarMapa} />
          {ponto && (
            <p className="mt-2 text-[14px]"><strong>Marcador:</strong> {ponto.latitude.toFixed(5)}, {ponto.longitude.toFixed(5)} · {municipioPonto ?? '…'}{precisao ? ` · ${precisao}` : ''}</p>
          )}
          {ponto && <label className="mt-2 flex items-start gap-2 text-[15px] font-semibold"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={confirmado} onChange={(e) => setConfirmado(e.target.checked)} />Confirmo que o marcador está no lugar certo</label>}
        </div>
      </Cartao>

      <Cartao titulo="2. Como chamar">
        <div className="grid gap-3 sm:grid-cols-2">
          <Campo rotulo="Nome do local"><input className={classeEntrada} maxLength={60} value={apelido} onChange={(e) => setApelido(e.target.value)} placeholder="Ex.: Casa da minha mãe" /></Campo>
          <Campo rotulo="Tipo"><select className={classeEntrada} value={tipo} onChange={(e) => setTipo(e.target.value as TipoLocal)}>{Object.entries(ROTULO_TIPO).map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></Campo>
          {tipo === 'Residencia' && (
            <label className="flex items-start gap-2 text-[14.5px] sm:col-span-2"><input type="checkbox" className="mt-1 size-5 accent-oceano" checked={moroAqui} onChange={(e) => setMoroAqui(e.target.checked)} />
              <span><strong>Eu moro aqui.</strong> Permite convidar familiares para acompanhar pedidos de socorro desta residência (só com sua aprovação).</span></label>
          )}
        </div>
      </Cartao>

      {tipo === 'Residencia' && (
        <Cartao titulo="3. Moradores (opcional)">
          <p className="mb-3 text-[13.5px] text-tinta-suave">Só números, sem nomes. Ajuda a equipe a saber quantas pessoas podem precisar de ajuda. Nunca aparece no mapa público; fica cifrado.</p>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-3">
            {campoMor('total', 'Moradores')}
            {campoMor('criancas', 'Crianças')}
            {campoMor('idosos', 'Idosos')}
            {campoMor('mobilidadeReduzida', 'Mobilidade reduzida')}
            {campoMor('dependemAssistencia', 'Dependem de assistência')}
          </div>
          <div className="mt-3 grid gap-3 sm:grid-cols-2">
            <Campo rotulo="Contato de referência (opcional)"><input className={classeEntrada} maxLength={120} value={mor.contatoReferencia ?? ''} onChange={(e) => setMor({ ...mor, contatoReferencia: e.target.value || null })} /></Campo>
            <Campo rotulo="Observações para a equipe (privadas)"><input className={classeEntrada} maxLength={500} value={mor.observacoes ?? ''} onChange={(e) => setMor({ ...mor, observacoes: e.target.value || null })} placeholder="Ex.: portão azul, acesso pela lateral" /></Campo>
          </div>
        </Cartao>
      )}

      {erro && <p role="alert" className="rounded-controle bg-emergencia-claro p-3 text-[14.5px] text-emergencia-escura">{erro}</p>}
      <Botao onClick={salvar} disabled={ocupado || !ponto || !confirmado || !apelido.trim()}>{id ? 'Salvar alterações' : 'Salvar local'}</Botao>
    </div>
  )
}
