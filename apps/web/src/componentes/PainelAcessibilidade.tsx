import { useState } from 'react'
import { ESCALAS, useAcessibilidade } from '../lib/acessibilidade'

export function BotaoAcessibilidade() {
  const { prefs, alterar } = useAcessibilidade()
  const [aberto, setAberto] = useState(false)
  const i = ESCALAS.indexOf(prefs.escala)
  return (
    <div className="relative">
      <button type="button" aria-expanded={aberto} aria-controls="painel-acessibilidade" onClick={() => setAberto(!aberto)}
        className="min-h-11 min-w-11 rounded-controle border border-linha bg-branco px-2.5 text-[16px] font-bold text-petroleo hover:bg-fundo">
        <span aria-hidden="true">Aa</span><span className="sr-only">Acessibilidade: texto, contraste, som e vibração</span>
      </button>
      {aberto && (
        <div id="painel-acessibilidade" role="dialog" aria-label="Acessibilidade" className="absolute right-0 z-40 mt-2 w-72 rounded-cartao bg-branco p-4 text-tinta shadow-lg ring-1 ring-linha">
          <p className="text-[15px] font-bold">Acessibilidade</p>
          <p className="mt-3 text-[13.5px] text-tinta-suave">Tamanho do texto: {Math.round(prefs.escala * 100)}%</p>
          <div className="mt-1 flex gap-2">
            <button type="button" className="min-h-11 flex-1 rounded-controle border border-linha text-[16px] font-bold disabled:opacity-40" disabled={i <= 0}
              onClick={() => alterar({ escala: ESCALAS[Math.max(0, i - 1)] })} aria-label="Diminuir texto">A−</button>
            <button type="button" className="min-h-11 flex-1 rounded-controle border border-linha text-[20px] font-bold disabled:opacity-40" disabled={i >= ESCALAS.length - 1}
              onClick={() => alterar({ escala: ESCALAS[Math.min(ESCALAS.length - 1, i + 1)] })} aria-label="Aumentar texto">A+</button>
          </div>
          <label className="mt-4 flex min-h-11 items-center gap-3 text-[15px]">
            <input type="checkbox" className="h-6 w-6" checked={prefs.altoContraste} onChange={(e) => alterar({ altoContraste: e.target.checked })} />
            Alto contraste
          </label>
          <label className="mt-3 flex min-h-11 items-center gap-3 text-[15px]">
            <input type="checkbox" className="h-6 w-6" checked={prefs.alarme} onChange={(e) => alterar({ alarme: e.target.checked })} />
            Alarme sonoro em emergências (pedido de socorro de quem você acompanha, aviso oficial, alagamento elevado)
          </label>
          <p className="text-[12.5px] text-tinta-suave">Com o app aberto toca mesmo no silencioso (iPhone com iOS 17 ou mais novo, Android). Com o app fechado, chega como notificação.</p>
          <label className="mt-3 flex min-h-11 items-center gap-3 text-[15px]">
            <input type="checkbox" className="h-6 w-6" checked={prefs.som} onChange={(e) => alterar({ som: e.target.checked })} />
            Sinal sonoro quando a atenção a alagamentos aumentar
          </label>
          {'vibrate' in navigator && (
            <label className="mt-3 flex min-h-11 items-center gap-3 text-[15px]">
              <input type="checkbox" className="h-6 w-6" checked={prefs.vibrar} onChange={(e) => alterar({ vibrar: e.target.checked })} />
              Vibrar quando a atenção aumentar
            </label>
          )}
          <p className="mt-1 text-[12.5px] text-tinta-suave">Todo alerta aparece também por escrito; nada é só sonoro.</p>
          <label className="mt-3 block text-[13.5px] text-tinta-suave">Voz da leitura
            <select className="mt-1 block min-h-11 w-full rounded-controle border border-linha bg-branco px-2 text-[15px] text-tinta" value={prefs.voz}
              onChange={(e) => alterar({ voz: e.target.value as 'auto' | 'aparelho' | 'servidor' })}>
              <option value="auto">Automática (aparelho; servidor se falhar)</option>
              <option value="aparelho">Somente a voz do aparelho</option>
              <option value="servidor">Voz do servidor (funciona no silencioso)</option>
            </select>
          </label>
          <button type="button" className="mt-3 min-h-11 w-full rounded-controle bg-petroleo text-[15px] font-semibold text-branco" onClick={() => setAberto(false)}>Fechar</button>
        </div>
      )}
    </div>
  )
}
