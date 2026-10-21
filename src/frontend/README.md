# Openspace frontend

The pixel-art building and the panels around it.

## Stack

- [Vite](https://vite.dev) + React + TypeScript
- [PixiJS](https://pixijs.com) v8 for the building, rendered without
  antialiasing so pixel art stays crisp
- Sprites (planned): PNG sheets with Aseprite JSON

## Structure

```
src/
  building/   PixiJS stage: floor, rooms, desks, agents, bubbles
  App.tsx     Layout: building plus side panels
```

## Run

```bash
npm install
npm run dev     # http://localhost:5173
npm run build
npm run lint
```

## Status

Draws an empty checkered floor that fills the window.
