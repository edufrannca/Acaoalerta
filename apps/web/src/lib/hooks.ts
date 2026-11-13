import { useCallback, useEffect, useRef, useState } from 'react'
import { api, ErroRede } from './api'
import { lerCache, salvarCache } from './dispositivo'

export function useOnline() {
  const [online, setOnline] = useState(typeof navigator === 'undefined' ? true : navigator.onLine)
  useEffect(() => {
    const on = () => setOnline(true)
    const off = () => setOnline(false)
    window.addEventListener('online', on)
    window.addEventListener('offline', off)
    return () => { window.removeEventListener('online', on); window.removeEventListener('offline', off) }
  }, [])
  return online
}

/** Busca com cache local em IndexedDB: offline mostra o último dado, identificado como tal. */
export function useDados<T>(caminho: string | null, opcoes: { cache?: boolean; intervaloMs?: number } = {}) {
  const [dados, setDados] = useState<T | null>(null)
  const [erro, setErro] = useState<string | null>(null)
  const [doCache, setDoCache] = useState<string | null>(null)
  const [carregando, setCarregando] = useState(!!caminho)
  const ativo = useRef(true)

  const carregar = useCallback(async () => {
    if (!caminho) return
    try {
      const r = await api<T>(caminho)
      if (!ativo.current) return
      setDados(r); setErro(null); setDoCache(null)
      if (opcoes.cache) void salvarCache(caminho, r)
    } catch (e) {
      if (!ativo.current) return
      if (opcoes.cache) {
        const c = await lerCache<T>(caminho)
        if (c) { setDados(c.valor); setDoCache(c.salvoEm) }
      }
      setErro(e instanceof ErroRede ? 'Sem conexão' : e instanceof Error ? e.message : 'Erro')
    } finally {
      if (ativo.current) setCarregando(false)
    }
  }, [caminho, opcoes.cache])

  useEffect(() => {
    ativo.current = true
    void carregar()
    const t = opcoes.intervaloMs ? setInterval(carregar, opcoes.intervaloMs) : undefined
    return () => { ativo.current = false; if (t) clearInterval(t) }
  }, [carregar, opcoes.intervaloMs])

  return { dados, erro, doCache, carregando, recarregar: carregar }
}

export interface Posicao { latitude: number; longitude: number; precisao: number | null; lidaEm: string }
export type EstadoGps = 'inativo' | 'solicitando' | 'ativo' | 'negado' | 'indisponivel'

/**
 * Localização via Geolocation API enquanto a página está aberta.
 * Não há rastreamento em segundo plano numa PWA. Atualizações respeitam intervalo mínimo (bateria).
 */
export function useLocalizacao(intervaloMinimoMs = 15000) {
  const [posicao, setPosicao] = useState<Posicao | null>(null)
  const [estado, setEstado] = useState<EstadoGps>('inativo')
  const ultima = useRef(0)
  const watch = useRef<number | null>(null)

  const iniciar = useCallback(() => {
    if (!('geolocation' in navigator)) { setEstado('indisponivel'); return }
    if (watch.current !== null) return
    setEstado('solicitando')
    watch.current = navigator.geolocation.watchPosition(
      (p) => {
        const agora = Date.now()
        if (agora - ultima.current < intervaloMinimoMs && ultima.current !== 0) return
        ultima.current = agora
        setEstado('ativo')
        setPosicao({ latitude: p.coords.latitude, longitude: p.coords.longitude, precisao: Math.round(p.coords.accuracy), lidaEm: new Date(p.timestamp).toISOString() })
      },
      (e) => setEstado(e.code === e.PERMISSION_DENIED ? 'negado' : 'indisponivel'),
      { enableHighAccuracy: true, maximumAge: 10000, timeout: 20000 },
    )
  }, [intervaloMinimoMs])

  useEffect(() => () => { if (watch.current !== null) navigator.geolocation.clearWatch(watch.current) }, [])

  // Reaproveita permissão já concedida sem pedir de novo.
  useEffect(() => {
    navigator.permissions?.query({ name: 'geolocation' as PermissionName }).then((p) => { if (p.state === 'granted') iniciar() }).catch(() => {})
  }, [iniciar])

  return { posicao, estado, iniciar }
}
