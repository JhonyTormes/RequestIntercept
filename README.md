# RequestIntercept

Proxy interceptador HTTP/HTTPS com MITM (Man-in-the-Middle) para inspecionar requisições de aplicações, similar ao proxy do Postman — **sem necessidade de instalação** de drivers ou componentes no sistema.

## Funcionalidades

- ✅ Interceptação **HTTP** e **HTTPS** com MITM (TLS 1.2)
- ✅ Geração automática de certificados TLS por host (assinados por CA local)
- ✅ Interface web para visualizar requisições em tempo real (atualização a cada 1.5s)
- ✅ Captura de **headers** e **body** (request e response)
- ✅ Suporte a JSON, XML, formulários, chunked encoding, binary bodies
- ✅ Pausar/retomar captura a qualquer momento
- ✅ Botão **"Ativar Proxy"** — ativa/desativa o proxy do Windows direto da UI
- ✅ **Instalar Certificado CA** com um clique (Trusted Root do Windows)
- ✅ **Filtro por URL** — digite texto para filtrar requisições na lista
- ✅ **Copiar como cURL** — gera o comando curl para CMD ou PowerShell
- ✅ **Reenviar requisição** — replay com um clique no painel de detalhes
- ✅ **Exportar HAR** — baixa todas as requisições no formato HTTP Archive
- ✅ Zero instalação — apenas executa e usa
- ✅ Single-file publish — gera um único `.exe` portável (~95 MB)

## Como usar

### Desenvolvimento (com .NET SDK)

```bash
dotnet run
```

### Publicação single-file (sem .NET SDK)

```bash
dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

Gera `publish\RequestIntercept.exe` — único arquivo, copie para qualquer pasta e execute.

### O que fazer depois de iniciar

```
=============================================
  RequestIntercept Proxy
=============================================
  Web UI:    http://localhost:4000
  Proxy:     0.0.0.0:8888

  1. Acesse a Web UI: http://localhost:4000
  2. Clique em "Ativar Proxy" no topo da pagina
  3. As requisicoes serao capturadas automaticamente

  Para HTTPS, instale o certificado CA:
     http://localhost:4000/api/certificate
=============================================
```

## Web UI

### Header

| Elemento | Descrição |
|----------|-----------|
| **Ativar/Desativar Proxy** | Ativa/desativa o proxy do Windows (HKCU\Internet Settings) |
| **Instalar Certificado CA** | Instala o certificado CA no Trusted Root do Windows |
| **Filtrar URL** | Campo de texto para filtrar requisições por host ou URL |
| **Pausar/Retomar** | Pausa ou retoma a captura de novas requisições |
| **Limpar** | Remove todas as requisições da lista |
| **Exportar HAR** | Baixa as requisições em formato HAR (HTTP Archive) |

### Painel de Detalhes

Ao clicar em uma requisição, o painel lateral exibe:

- **General** — método, URL, status, hora, duração, protocolo
- **Request Headers** — todos os headers enviados
- **Request Body** — corpo da requisição (se houver)
- **Response Headers** — todos os headers recebidos
- **Response Body** — corpo da resposta com syntax highlight para JSON

Ações disponíveis no painel:

| Ação | Descrição |
|------|-----------|
| **Reenviar** | Reenvia a requisição original ao servidor e mostra a nova resposta |
| **Copiar Curl (CMD)** | Copia o comando curl para Windows Command Prompt |
| **Copiar Curl (PowerShell)** | Copia o comando curl.exe para PowerShell |

### Corpos binários

Se o `content-type` não for texto (`text/*`, `application/json`, `application/xml`, etc.) ou o conteúdo tiver mais de 30% de caracteres não-printáveis, o body é exibido como `[Binary data: X bytes]` em vez de caracteres corrompidos.

## API Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| `GET` | `/api/requests` | Lista resumida das requisições |
| `GET` | `/api/requests/{id}` | Detalhes completos de uma requisição |
| `DELETE` | `/api/requests` | Limpa todas as requisições |
| `GET` | `/api/requests/export` | Exporta em formato HAR |
| `POST` | `/api/requests/{id}/replay` | Reenvia a requisição |
| `GET` | `/api/status` | Status do proxy e contador |
| `POST` | `/api/pause` | Pausa a captura |
| `POST` | `/api/resume` | Retoma a captura |
| `GET` | `/api/proxy` | Status do proxy do Windows |
| `POST` | `/api/proxy/enable` | Ativa proxy do Windows |
| `POST` | `/api/proxy/disable` | Desativa proxy do Windows |
| `GET` | `/api/certificate` | Download do certificado CA (.crt) |
| `POST` | `/api/certificate/install` | Instala CA no Trusted Root do Windows |

## Testando com curl

```bash
# HTTP (via proxy explicito)
curl -x http://localhost:8888 http://httpbin.org/get

# HTTPS (ignore certificado auto-assinado com -k)
curl -x http://localhost:8888 -k https://httpbin.org/get

# HTTPS com CA instalado (sem -k)
curl -x http://localhost:8888 https://httpbin.org/get
```

## Como ativar o proxy do Windows manualmente

1. Abra **Configurações** → **Rede e Internet** → **Proxy**
2. Ative "Usar servidor proxy"
3. Endereço: `localhost` | Porta: `8888`

Ou use o botão **"Ativar Proxy"** na Web UI.

## Instalação do certificado CA (para HTTPS sem warnings)

### Pela Web UI
Clique em **"Instalar Certificado CA"** no topo da página (requer Admin).

### Manualmente
1. Baixe o certificado em http://localhost:4000/api/certificate
2. Abra o arquivo `.crt` → **Instalar Certificado**
3. Escolha **Computador Local** → **Armazenar em...**
4. Selecione **Autoridades de Certificação Raiz Confiáveis**

## Como funciona

```
┌─────────────┐    CONNECT host:443     ┌──────────────────┐    TLS     ┌──────────────┐
│  Aplicação   │ ─────────────────────── │  RequestIntercept │ ────────→ │  Servidor    │
│  (HttpClient)│ ←────────────────────── │  (Proxy MITM)    │ ←──────── │  (httpbin)   │
└─────────────┘   200 OK + TLS mit cert  └──────────────────┘           └──────────────┘
                         │
                         ▼
                   ┌─────────────┐
                   │  Web UI     │
                   │  :4000      │
                   └─────────────┘
```

O proxy atua como um servidor TLS intermediário:

1. A aplicação faz uma requisição **CONNECT** para o proxy (porta 8888)
2. O proxy gera um certificado TLS para o host alvo (assinado pela CA local)
3. Estabelece TLS com a aplicação (decifrando o tráfego)
4. Encaminha a requisição para o servidor real via TLS
5. Retorna a resposta para a aplicação
6. Toda a troca fica registrada na interface web (porta 4000)

Para requisições **HTTP** (sem TLS), o proxy recebe a URL absoluta, extrai o host e path, conecta ao servidor de destino e faz o relay.

## Estrutura do projeto

```
RequestIntercept/
├── Program.cs                          # ASP.NET Minimal API + endpoints
├── RequestIntercept.csproj             # Projeto .NET 9
├── appsettings.json                    # Configuração (portas, logging)
├── Models/
│   └── InterceptedRequest.cs           # Modelo de dados da requisição
├── Services/
│   ├── CertificateService.cs           # Geração de CA e certificados TLS
│   ├── ProxyService.cs                 # Proxy TCP (HTTP + HTTPS MITM)
│   └── RequestStore.cs                 # Armazenamento em memória
└── wwwroot/
    ├── index.html                      # Interface web
    ├── style.css                       # Estilos (tema escuro)
    └── app.js                          # Lógica da UI (polling, filtro, etc.)
```

## Tecnologias

- **.NET 9** — ASP.NET Core Minimal API + BackgroundService
- **System.Net.Security.SslStream** — TLS 1.2 server/client
- **System.Net.Sockets.TcpListener** — proxy TCP
- **System.Security.Cryptography** — geração de certificados RSA 4096/2048
- **Microsoft.Win32.Registry** — ativação do proxy do Windows
- **HTML/CSS/JS vanilla** — interface web sem frameworks

## Licença

MIT
