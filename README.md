# Vini ♥ Line

Um jogo web mobile, relaxante e sem fim: Vini lança corações e Line os recolhe.

## Testar localmente

O jogo pode ser aberto diretamente pelo `index.html`. Para testar também o modo offline/PWA, use um servidor local:

```bash
python3 -m http.server 8080
```

Depois, acesse `http://localhost:8080`. Arraste, toque ou use o mouse para mover a cesta.

## Estrutura

- `index.html` — interface e metadados mobile;
- `style.css` — apresentação, abertura e acessibilidade;
- `game.js` — desenho, física, progressão, áudio e persistência;
- `manifest.json` e `service-worker.js` — instalação e funcionamento offline;
- `assets/` — ícone e futuros recursos de áudio/arte.

## Publicar

Envie os arquivos da raiz para qualquer hospedagem estática (GitHub Pages, Netlify, Cloudflare Pages etc.). HTTPS é necessário para instalar o PWA e usar o service worker fora de `localhost`.

## Personalizar

As cores e a interface ficam em `style.css`; ritmos, tipos de coração e animações ficam nas constantes no início de `game.js`. O ícone pode ser trocado em `assets/icon.svg`. O áudio atual é sintetizado e não exige arquivos; sons próprios podem ser adicionados em `assets/audio/`.
