import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Route, Routes } from 'react-router'
import './estilos.css'
import { Estrutura } from './componentes/Estrutura'
import { iniciarSincronizacaoAutomatica } from './lib/filaOffline'
import { acompanharNovaVersao } from './lib/versao'
import { ProvedorSessao } from './lib/sessao'
import { Inicio } from './paginas/Inicio'
import { AcompanharSocorro } from './paginas/Socorro'
import { EstouSeguro } from './paginas/EstouSeguro'
import { QueroAjudar } from './paginas/QueroAjudar'
import { PaginaMapa } from './paginas/PaginaMapa'
import { MeusPedidos } from './paginas/MeusPedidos'
import { Perfil } from './paginas/Perfil'
import { Painel } from './paginas/Painel'
import { CentralSimulada } from './paginas/CentralSimulada'
import { Demonstracao } from './paginas/Demonstracao'
import { Instalar } from './paginas/Instalar'
import { Alertas } from './paginas/Alertas'
import { Abrigos } from './paginas/Abrigos'
import { Retransmitir } from './paginas/Retransmitir'
import { ProvedorAcessibilidade } from './lib/acessibilidade'

iniciarSincronizacaoAutomatica()
acompanharNovaVersao()

createRoot(document.getElementById('raiz')!).render(
  <StrictMode>
    <ProvedorAcessibilidade>
    <ProvedorSessao>
      <BrowserRouter>
        <Estrutura>
          <Routes>
            <Route path="/" element={<Inicio />} />
            <Route path="/socorro/:id" element={<AcompanharSocorro />} />
            <Route path="/seguro" element={<EstouSeguro />} />
            <Route path="/ajudar" element={<QueroAjudar />} />
            <Route path="/mapa" element={<PaginaMapa />} />
            <Route path="/pedidos" element={<MeusPedidos />} />
            <Route path="/perfil" element={<Perfil />} />
            <Route path="/painel" element={<Painel />} />
            <Route path="/central/:codigo" element={<CentralSimulada />} />
            <Route path="/demo" element={<Demonstracao />} />
            <Route path="/instalar" element={<Instalar />} />
            <Route path="/alertas" element={<Alertas />} />
            <Route path="/abrigos" element={<Abrigos />} />
            <Route path="/retransmitir" element={<Retransmitir />} />
            <Route path="*" element={<p className="py-10 text-center">Página não encontrada.</p>} />
          </Routes>
        </Estrutura>
      </BrowserRouter>
    </ProvedorSessao>
    </ProvedorAcessibilidade>
  </StrictMode>,
)
