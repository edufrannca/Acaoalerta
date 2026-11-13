import { useState } from 'react'
import { Cartao, classeEntrada } from '../componentes/Base'
import { Mapa, ROTULOS_CAMADAS, type Camada } from '../componentes/Mapa'
import { useLocalizacao } from '../lib/hooks'
import { MUNICIPIOS } from '../lib/municipios'

const TODAS: Camada[] = ['limites', 'avisos', 'chuva', 'areasRisco', 'abrigos', 'relatos']

export function PaginaMapa() {
  const [camadas, setCamadas] = useState<Camada[]>(TODAS)
  const [municipio, setMunicipio] = useState('')
  const { posicao, iniciar, estado } = useLocalizacao()
  const alternar = (c: Camada) => setCamadas((l) => (l.includes(c) ? l.filter((x) => x !== c) : [...l, c]))
  return (
    <div className="grid gap-4 lg:grid-cols-[280px_1fr]">
      <Cartao titulo="Filtros">
        <label className="block">
          <span className="text-[14px] font-medium">Município</span>
          <select className={classeEntrada} value={municipio} onChange={(e) => setMunicipio(e.target.value)}>
            <option value="">Toda a RMR (14 municípios)</option>
            {Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
          </select>
        </label>
        <fieldset className="mt-4">
          <legend className="text-[14px] font-medium">Camadas</legend>
          {TODAS.map((c) => (
            <label key={c} className="mt-2 flex items-center gap-2 text-[14.5px]">
              <input type="checkbox" className="size-5 accent-oceano" checked={camadas.includes(c)} onChange={() => alternar(c)} />{ROTULOS_CAMADAS[c]}
            </label>
          ))}
        </fieldset>
        <button onClick={iniciar} className="mt-4 text-[14px] font-medium text-oceano underline-offset-2 hover:underline">
          {estado === 'ativo' ? 'Localização ativa' : 'Mostrar minha posição'}
        </button>
        <p className="mt-4 text-[12.5px] text-tinta-suave">
          Cada camada tem sua fonte e horário. Chuva: APAC (atualizada a cada 5 min no servidor). Relatos: agregados de ~1 km, sem dados pessoais. Ausência de marcação não significa ausência de risco.
        </p>
      </Cartao>
      <Mapa camadas={camadas} municipio={municipio || null} posicao={posicao} altura="h-[64vh] min-h-[420px]" />
    </div>
  )
}
