# RequestIntercept

Proxy interceptador HTTP/HTTPS com MITM (Man-in-the-Middle) para inspecionar requisições de aplicações, similar ao proxy do Postman — **sem necessidade de instalação** de drivers ou componentes no sistema.

## Funcionalidades

- ✅ Interceptação **HTTP** e **HTTPS** com MITM
- ✅ Geração automática de certificados TLS por host
- ✅ Interface web para visualizar requisições em tempo real
- ✅ Captura de **headers** e **body** (request e response)
- ✅ Suporte a JSON, formulários, chunked encoding
- ✅ Pausar/retomar captura
- ✅ Download do certificado CA para inspeção HTTPS
- ✅ Zero instalação — apenas executa e usa

## Como usar

```bash
# Iniciar o servidor
dotnet run

# O servidor inicia com:
#   Web UI:  http://localhost:4000
#   Proxy:   http://localhost:8888
```

### Testando com curl

```bash
# HTTP
curl -x http://localhost:8888 http://httpbin.org/get

# HTTPS (use -k por causa do certificado auto-assinado)
curl -x http://localhost:8888 -k https://httpbin.org/get
```

### Configurando no Windows

1. Abra **Configurações** → **Rede e Internet** → **Proxy**
2. Ative "Usar servidor proxy"
3. Endereço: `localhost` | Porta: `8888`
4. Acesse `http://localhost:4000` para ver as requisições

### Para inspeção HTTPS sem warnings

1. Baixe o certificado CA em http://localhost:4000/api/certificate
2. Instale como **Autoridade de Certificação Raiz Confiável**:
   - Abra o arquivo .crt → **Instalar Certificado**
   - Escolha **Computador Local** → **Armazenar em...**
   - Selecione **Autoridades de Certificação Raiz Confiáveis**

## Como funciona

```
┌─────────────┐    CONNECT host:443     ┌──────────────────┐    TLS     ┌──────────────┐
│  Aplicação   │ ─────────────────────── │  RequestIntercept │ ────────→ │  Servidor    │
│  (curl, app) │ ←────────────────────── │  (Proxy MITM)    │ ←──────── │  (httpbin)   │
└─────────────┘   200 OK + TLS mit cert  └──────────────────┘           └──────────────┘
                         │
                         ▼
                   ┌─────────────┐
                   │  Web UI     │
                   │  :4000      │
                   └─────────────┘
```

O proxy atua como um servidor TLS intermediário:
1. A aplicação faz uma requisição CONNECT para o proxy
2. O proxy gera um certificado para o host alvo (assinado pela CA local)
3. Estabelece TLS com a aplicação (decifrando o tráfego)
4. Encaminha a requisição para o servidor real via TLS
5. Retorna a resposta para a aplicação
6. Toda a troca fica registrada na interface web

## Tecnologias

- .NET 9 (ASP.NET Core Minimal API)
- System.Net.Security.SslStream (TLS)
- System.Security.Cryptography (certificados)
- HTML/CSS/JS vanilla (sem frameworks)

## Licença

MIT
