import { useState } from 'react'
import { Cartao, Selo, classeEntrada } from '../componentes/Base'
import { IcLocal } from '../componentes/Icones'
import { formatarHora } from '../lib/api'
import { useDados, useLocalizacao } from '../lib/hooks'
import { MUNICIPIOS, nomeMunicipio } from '../lib/municipios'
import { distanciaKm, formatarKm, type RespostaAbrigos } from '../lib/regiao'

const ACESS: Record<string, string> = { Possui: 'Possui acessibilidade (informado pela fonte)', Inexistente: 'Sem acessibilidade (informado pela fonte)', NaoInformada: 'Acessibilidade não informada' }

export function Abrigos() {
  const { posicao, iniciar, estado } = useLocalizacao()
  const [municipio, setMunicipio] = useState('')
  const { dados, doCache } = useDados<RespostaAbrigos>('/api/abrigos', { cache: true, intervaloMs: 10 * 60 * 1000 })
  const lista = (dados?.abrigos ?? []).filter((a) => !municipio || a.municipioCodigo === municipio)
    .map((a) => ({ ...a, km: posicao ? distanciaKm(posicao, a) : null })).sort((a, b) => (a.km ?? 0) - (b.km ?? 0) || a.nome.localeCompare(b.nome))
  const f = dados?.fonte
  return (
    <div className="mx-auto grid max-w-3xl gap-4">
      <h1 className="text-[26px] font-bold text-petroleo">Abrigos</h1>
      <Cartao>
        <p className="text-[15px]">Somente abrigos <strong>ativos</strong> publicados por fontes oficiais. Constar na lista <strong>não garante vagas</strong>: confirme com a Defesa Civil (199) antes de se deslocar.</p>
        <p className="mt-1 text-[13.5px] text-tinta-suave">{dados?.cobertura}</p>
        {f && <p className="mt-1 text-[13.5px] text-tinta-suave">Fonte: {f.nome} · {f.disponivel ? `consultada ${formatarHora(f.ultimaConsultaEm)}` : f.ultimoSucessoEm ? `indisponível agora; último dado de ${formatarHora(f.ultimoSucessoEm)}` : 'indisponível no momento'}</p>}
        {doCache && <Selo tom="atencao">Cópia salva no aparelho</Selo>}
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <select aria-label="Município" className={`${classeEntrada} w-auto`} value={municipio} onChange={(e) => setMunicipio(e.target.value)}>
            <option value="">Todos os municípios</option>
            {Object.entries(MUNICIPIOS).map(([c, n]) => <option key={c} value={c}>{n}</option>)}
          </select>
          {!posicao && estado !== 'solicitando' && <button type="button" onClick={iniciar} className="min-h-11 rounded-controle bg-petroleo px-4 text-[15px] font-semibold text-branco">Ordenar por distância</button>}
        </div>
      </Cartao>
      {lista.length === 0 ? (
        <Cartao><p className="text-[15.5px]">Nenhum abrigo ativo publicado {municipio ? `em ${nomeMunicipio(municipio)}` : 'nas fontes consultadas'} neste momento. As prefeituras abrem abrigos durante emergências; em caso de necessidade, ligue 199.</p></Cartao>
      ) : (
        <ul className="grid gap-3">
          {lista.map((a) => (
            <li key={a.id} className="rounded-cartao bg-branco p-4 ring-1 ring-linha">
              <div className="flex flex-wrap items-start justify-between gap-2">
                <h2 className="text-[18px] font-bold text-petroleo">{a.nome}</h2>
                <span className="rounded-full bg-atencao-claro px-2.5 py-1 text-[12.5px] font-semibold text-atencao">{a.disponibilidade}</span>
              </div>
              <p className="text-[15px]">{a.endereco ?? 'Endereço não informado pela fonte'} · {nomeMunicipio(a.municipioCodigo)} - PE</p>
              {a.km != null && <p className="flex items-center gap-1 text-[14px] text-tinta-suave"><IcLocal tamanho={16} />{formatarKm(a.km)} em linha reta</p>}
              <dl className="mt-2 grid gap-x-3 text-[13.5px] sm:grid-cols-[170px_1fr]">
                <dt className="font-semibold">Status publicado</dt><dd>{a.statusFonte ?? 'não informado'}{a.estagioOperacional ? ` · ${a.estagioOperacional}` : ''}</dd>
                <dt className="font-semibold">Acessibilidade</dt><dd>{ACESS[a.acessibilidade] ?? a.acessibilidade}</dd>
                <dt className="font-semibold">Coordenadas</dt><dd className="numero-tabular">{a.latitude.toFixed(5)}, {a.longitude.toFixed(5)}</dd>
                <dt className="font-semibold">Fonte</dt><dd>{a.fonte}{a.atualizadoNaFonteEm ? `, atualizado em ${formatarHora(a.atualizadoNaFonteEm)}` : ''}</dd>
              </dl>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
