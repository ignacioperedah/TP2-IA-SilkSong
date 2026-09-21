# SilksongRL — Imitation Learning Dataset Logger

Fork de [jimmie-jams/SilksongRL](https://github.com/jimmie-jams/SilksongRL), un mod de BepInEx +
servidor Python que entrena un agente de **reinforcement learning** para pelear contra bosses de
*Hollow Knight: Silksong*.

**Este fork tiene un objetivo distinto.** Es un proyecto de la materia Inteligencia Artificial
(UTN FRBA). En vez de entrenar un agente por RL, el mod se usó para **loggear un dataset
supervisado**: en cada tick del combate contra Lace (fase 1), se registra el estado del juego
junto con el input real del jugador (yo, jugando con un control 8BitDo). Ese CSV se usa después,
fuera de este repo, para entrenar una red neuronal de clasificación que prediga qué acción tomaría
un jugador humano dado el estado del juego — sin loop de RL, sin servidor, sin recompensas.

Todo el pipeline de RL original (servidor Python, protocolo de sockets, loop de entrenamiento con
PPO) fue eliminado de este fork porque no hace falta para este objetivo. Ver
[ARCHITECTURE.md](ARCHITECTURE.md) para el detalle de qué se sacó, qué se reusó tal cual, y las
decisiones de diseño (y bugs encontrados) durante la adaptación.

## Qué hace el mod ahora

Con el toggle `O` activado, en cada tick del combate contra Lace 1 se escribe una fila a un CSV
con:
- El vector de observación del encuentro (posición/velocidad/vida de Hornet y del boss, categoría
  de ataque activa del boss — 21 floats normalizados a [0,1]).
- La acción real del jugador leída del gamepad (`move`, `look`, `jump`, `attack`, `dash`).
- Metadata (`episode_id`, `tick`, `outcome`) para poder separar intentos completos.

No se manda nada por socket, no hay agente, no hay reward. Jugás normal contra Lace 1 y el mod
graba.

## Setup

### Prerequisitos

- **Hollow Knight: Silksong**
- **BepInEx 5.4.x** ([Thunderstore](https://thunderstore.io/c/hollow-knight-silksong/p/BepInEx/BepInExPack_Silksong/))
- **Debug Mod** en `BepInEx/plugins/` ([hk-speedrunning/Silksong.DebugMod](https://github.com/hk-speedrunning/Silksong.DebugMod)) — se usa para el SaveState y el quickload por F5
- Un control (el mapeo de botones default asume un pad tipo Xbox/XInput — ver [ARCHITECTURE.md](ARCHITECTURE.md#mapeo-de-acciones))
- **.NET Framework 4.7.2** + Visual Studio (o cualquier build system compatible con MSBuild), solo si vas a compilar el mod vos mismo

### Compilar el mod

```bash
cd unity-mod/SilksongRL
copy SilksongRL.csproj.user.example SilksongRL.csproj.user
```

Editá `SilksongRL.csproj.user` y descomentá/ajustá `<GameDir>` con la ruta de tu instalación de
Silksong. Abrí `unity-mod/SilksongRL.sln` en Visual Studio y compilá (Ctrl+Shift+B). El `.csproj`
tiene un post-build event que copia el `.dll` compilado directo a `BepInEx/plugins/` de tu
`GameDir` — no hace falta copiarlo a mano. **Cerrá el juego antes de compilar**, si no el `.dll`
va a quedar bloqueado y el copy va a fallar.

### Preparar el encuentro (una sola vez)

1. Abrí el juego con el mod instalado y cargá tu partida.
2. Con Debug Mod (F2 para el overlay), usá noclip para llegar hasta Lace y activar la pelea.
3. Pausá y presioná **Write** para guardar un SaveState del inicio de la pelea.
4. Presioná **Read** para cargar ese SaveState al Quickslot.
5. Activá **"Load Quickslot on Death"**.
6. Bindeá **Quickslot (Load) a F5** — así es como el mod reinicia el intento automáticamente
   cuando morís o le ganás a Lace.
7. Cerrá el overlay de Debug Mod (F2).

De ahí en más, alcanza con tocar F5 una vez para volver a este punto de partida en cualquier
sesión futura.

### Loggear el dataset

1. Con el juego en el punto de partida de la pelea, tocá **O** para activar el logging (vas a ver
   `[RL] Logging enabled` en la consola de BepInEx).
2. Jugá contra Lace normal, con tu control. Ganes o pierdas, el mod detecta el resultado, guarda
   la fila final del intento con el `outcome` correspondiente, y dispara el F5 automático para
   arrancar el siguiente intento.
3. Tocá **O** de nuevo para pausar el logging (esto también cierra el archivo, así lo podés abrir
   en Excel/pandas sin que quede bloqueado por el juego).

El CSV se escribe en `<carpeta del juego>/SilksongRL_Dataset/lace1_dataset.csv`. Formato completo
de columnas en [ARCHITECTURE.md](ARCHITECTURE.md#formato-del-csv).

## Limitaciones conocidas / decisiones de scope

- **Solo Lace 1.** El esquema de observación (columnas de ataque) y el tamaño del vector están
  hardcodeados para ese encuentro. `TrainingEpisodeManager`/`IBossEncounter` son genéricos y
  soportan otros bosses (heredado del proyecto original), pero `DatasetLogger` no — usar otro
  `TargetBoss` va a generar un warning y saltear filas por tamaño de observación inesperado.
- **Lanza Sedeña (ranged) queda afuera del dataset.** No hay columna para esa acción; la decisión
  fue autolimitarse a no usarla durante la recolección en vez de agregar ambigüedad a las
  etiquetas.
- **Agujolín y curación tampoco se loggean.**
- **Asume bind de gamepad tipo Xbox/XInput** (stick izquierdo, A, X, RT). Si usás otro layout, hay
  que ajustar `Logging/HumanInputReader.cs`.
- **Clases desbalanceadas.** La combinación "sin input" va a ser, por lejos, la más común en el
  dataset. Tenerlo en cuenta al armar la matriz de confusión / métricas del clasificador.

## Créditos y licencia

Basado en el trabajo de [jimmie-jams](https://github.com/jimmie-jams/SilksongRL) (arquitectura del
mod, extracción de observaciones, detección de episodios, integración con BepInEx/Harmony). MIT
License — ver [LICENSE](LICENSE).
