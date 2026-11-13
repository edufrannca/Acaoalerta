import * as maplibregl from 'maplibre-gl'
import { useEffect, useRef } from 'react'
import { estilo } from './Mapa'

export interface Ponto { latitude: number; longitude: number }

/**
 * Escolha de um ponto no mapa: toque/clique posiciona, o marcador pode ser arrastado e as setas do teclado
 * movem o ponto (acessível sem mouse). Nada é salvo aqui: quem usa confirma antes.
 */
export function SeletorMapa({ ponto, aoMudar, centro, zoom = 15, altura = 'h-[320px]' }: {
  ponto: Ponto | null; aoMudar: (p: Ponto) => void; centro?: Ponto | null; zoom?: number; altura?: string
}) {
  const div = useRef<HTMLDivElement>(null)
  const mapa = useRef<maplibregl.Map | null>(null)
  const marcador = useRef<maplibregl.Marker | null>(null)
  const aoMudarRef = useRef(aoMudar)
  aoMudarRef.current = aoMudar

  useEffect(() => {
    if (!div.current) return
    const inicio = ponto ?? centro
    const m = new maplibregl.Map({ container: div.current, style: estilo, center: inicio ? [inicio.longitude, inicio.latitude] : [-34.95, -8.05], zoom: inicio ? (ponto ? zoom : 13) : 9.5, attributionControl: { compact: true } })
    m.addControl(new maplibregl.NavigationControl({ showCompass: false }), 'top-right')
    m.on('click', (e) => aoMudarRef.current({ latitude: Number(e.lngLat.lat.toFixed(6)), longitude: Number(e.lngLat.lng.toFixed(6)) }))
    mapa.current = m
    return () => { m.remove(); mapa.current = null; marcador.current = null }
  }, []) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const m = mapa.current
    if (!m) return
    if (!ponto) { marcador.current?.remove(); marcador.current = null; return }
    if (!marcador.current) {
      marcador.current = new maplibregl.Marker({ draggable: true, color: '#B4472F' }).setLngLat([ponto.longitude, ponto.latitude]).addTo(m)
      marcador.current.on('dragend', () => { const l = marcador.current!.getLngLat(); aoMudarRef.current({ latitude: Number(l.lat.toFixed(6)), longitude: Number(l.lng.toFixed(6)) }) })
    } else marcador.current.setLngLat([ponto.longitude, ponto.latitude])
    if (!m.getBounds().contains([ponto.longitude, ponto.latitude]) || m.getZoom() < 12) m.flyTo({ center: [ponto.longitude, ponto.latitude], zoom, duration: 600 })
  }, [ponto?.latitude, ponto?.longitude]) // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    if (centro && !ponto) mapa.current?.flyTo({ center: [centro.longitude, centro.latitude], zoom: 13, duration: 600 })
  }, [centro?.latitude, centro?.longitude]) // eslint-disable-line react-hooks/exhaustive-deps

  const mover = (dLat: number, dLon: number) => { if (ponto) aoMudar({ latitude: Number((ponto.latitude + dLat).toFixed(6)), longitude: Number((ponto.longitude + dLon).toFixed(6)) }) }
  return (
    <div>
      <div ref={div} role="application" tabIndex={0} aria-label="Mapa para escolher o local. Toque no mapa para marcar; arraste o marcador ou use as setas do teclado para ajustar."
        onKeyDown={(e) => {
          const passo = 0.0001
          if (e.key === 'ArrowUp') { e.preventDefault(); mover(passo, 0) } else if (e.key === 'ArrowDown') { e.preventDefault(); mover(-passo, 0) }
          else if (e.key === 'ArrowLeft') { e.preventDefault(); mover(0, -passo) } else if (e.key === 'ArrowRight') { e.preventDefault(); mover(0, passo) }
        }}
        className={`${altura} w-full overflow-hidden rounded-cartao ring-1 ring-linha focus-visible:outline-3 focus-visible:outline-oceano`} />
      <p className="mt-1 text-[12.5px] text-tinta-suave">Toque no mapa para marcar. Arraste o marcador vermelho para ajustar. Mapa © colaboradores do OpenStreetMap.</p>
    </div>
  )
}
