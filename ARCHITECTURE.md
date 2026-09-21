# Arquitectura y decisiones de diseño

Este documento unifica las notas de adaptación que se fueron generando durante el desarrollo
(`logging_adaptation_spec.md`, `gamepad_input_fix.md`, `action_schema_final.md` — ya no existen
como archivos separados, su contenido está acá). Explica qué se reusó del proyecto original de
[jimmie-jams](https://github.com/jimmie-jams/SilksongRL), qué se sacó, qué se agregó, y los bugs
no triviales que aparecieron en el camino.

## 1. Qué se reusa tal cual del proyecto original

- **`Encounters/LaceEncounter.cs`** (y `IBossEncounter`, `LaceSecondEncounter`,
  `SavageBeastflyEncounter`): definen el vector de observación de cada boss, la detección de
  "hero stuck", `GetMaxHP()`, etc. `ExtractObservationArray(hero, boss)` para Lace 1 devuelve 21
  floats normalizados a [0,1]: `heroX, heroY, heroVelX, heroVelY, heroHP, bossX, bossY, bossVelX,
  bossVelY, bossHP` (10) + one-hot de 11 categorías de ataque de Lace (`Idle, ComboSlash, Counter,
  RapidSlash, JSlash, Downstab, Charge, Evade, CrossSlash, Stun, Multihit`).
- **`TrainingEpisodeManager.cs`**: detecta muerte del héroe, muerte del boss, y "hero stuck", y
  dispara el reset automático simulando F5. Se mantiene, con dos fixes puntuales — ver sección 5.
- **Los Harmony patches de `RLManager`** que trackean `Hero`/`Boss` vía `HeroController.Awake()` /
  `HealthManager.Awake()`.
- **`ActionManager.Action`** (clase) y los enums `MoveDirection`/`LookDirection`/`ActionSpaceType`.
  `ActionSpaceType` sigue existiendo porque `IBossEncounter.GetActionSpaceType()` todavía lo usa,
  aunque el logging ya no depende de él (ver sección 3).

## 2. Qué se sacó y por qué

El proyecto original tiene dos funciones en paralelo dentro de `RLManager.FixedUpdate()`: correr
un loop de RL (pedirle una acción a un servidor Python por socket y ejecutarla) o loggear. Para
este fork, todo lo que sólo servía al loop de RL se eliminó:

- **`python-client/`** completo (servidor de sockets, `rl_core.py`, `sb3_ppo_override.py`,
  `launch.py`, checkpoints entrenados). El entrenamiento del clasificador es un script aparte,
  fuera de este repo, que lee el CSV con pandas/sklearn/pytorch — no necesita nada de esto.
- **`unity-mod/SilksongRL/SocketClient.cs`** — cliente del protocolo de sockets.
- **En `RLManager.cs`**: el toggle `P` (control por agente), `StepRLAsync`, `InitializeClientAsync`,
  todo el tracking de transiciones para RL (`previousObservations`, `previousAction`,
  `hasPreviousStep`, `pendingDoneTransition`, `whoDied`), `OnGUI` (mostraba el ping del socket), y
  la config de host/puerto/eval mode.
- **En `ActionManager.cs`**: `ButtonControlPatch` (el parche de Harmony que pisaba el input real
  con la acción del agente — nunca debía usarse en modo logging, así que directamente se sacó),
  `GetKeyState`, `ArrayToAction`, `ActionToArray`, `GetActionSpaceShape` (todo esto era el
  protocolo de conversión Action↔array para el servidor).

**Qué NO se tocó, aunque quedó parcialmente sin uso:** `IBossEncounter.CalculateReward()` (reward
shaping para RL) y los métodos de observación híbrida/visual (`GetObservationType`,
`GetVisualObservationSize`, `GetScreenCapture`, el sistema en `Vision/`) se dejaron como están.
Son parte de la interfaz de cada encounter y tocarlos implicaba editar las cuatro clases de
`Encounters/` sin ningún beneficio real para el logging — quedan como código heredado, no
estorban.

## 3. Qué se agregó

- **`Logging/HumanInputReader.cs`**: lee el control físico del jugador. Ver sección 4 (mapeo) y
  sección 5 (por qué no lee teclado).
- **`Logging/DatasetLogger.cs`**: escribe una fila por tick a un CSV en modo append (header solo
  si el archivo no existe, flush inmediato por fila). Ver sección 6 (formato).
- **`RLManager.isLoggingEnabled`** (toggle `O`, independiente de cualquier control por agente —
  que ya ni existe en este fork) y la rama correspondiente en `FixedUpdate()`.
- **Un renglón de cambio en `ActionManager.GetKeyDownPatch`**: el guard pasó de chequear
  `isAgentControlEnabled` a chequear `isLoggingEnabled`, para que el F5 simulado por
  `TrainingEpisodeManager` funcione también en modo logging.

## 4. Mapeo de acciones (gamepad)

El input real se lee del **Input System nuevo de Unity** (`UnityEngine.InputSystem.Gamepad`), no
del teclado — ver sección 5 para el porqué. Control probado: 8BitDo en modo Xbox/XInput.

| Acción del mod       | Control físico          | API                                          |
|-----------------------|--------------------------|-----------------------------------------------|
| `move` izq/der        | Stick izquierdo (eje X)  | `Gamepad.current.leftStick.left` / `.right`  |
| `look` arriba/abajo   | Stick izquierdo (eje Y)  | `Gamepad.current.leftStick.up` / `.down`     |
| `jump`                | A                        | `Gamepad.current.buttonSouth`                |
| `attack`              | X                        | `Gamepad.current.buttonWest`                 |
| `dash`                | RT                       | `Gamepad.current.rightTrigger`               |

`dash` se loggea siempre, **sin** depender de `ActionSpaceType.Extended` (ese enum es maquinaria
del protocolo RL original — `LaceEncounter.GetActionSpaceType()` devuelve `Basic` hardcodeado, así
que si el logging siguiera gateado por eso, `dash` nunca se hubiera registrado en esta pelea).
`HumanInputReader.ReadCurrentAction()` no recibe `ActionSpaceType` como parámetro: el logging está
totalmente desacoplado del espacio de acciones del mod RL original.

**Fuera del dataset a propósito:** Lanza Sedeña (ranged), Agujolín, curación. No hay columnas para
estas acciones — la decisión fue autolimitarse a no usarlas durante la recolección en vez de
agregar ambigüedad a las etiquetas.

`leftStick.left/right/up/down` son controles booleanos que Unity ya deriva del stick analógico con
su propio umbral (press point) — no hace falta programar un deadzone manual.

## 5. Bugs encontrados durante la adaptación

### 5.1. Legacy `Input` sí funciona, pero no para gamepad

La primera versión de `HumanInputReader` leía `UnityEngine.Input.GetKey(KeyCode...)` (API legacy).
Se confirmó con un log temporal que esta API sí refleja el input físico real en este build (el
propio mod ya la usaba para F5 vía `Input.GetKeyDown`, gateada solo para esa tecla). El problema
apareció después: el jugador usa un control 8BitDo, nunca teclado, así que `move/look/jump/attack`
quedaban siempre en 0 en el CSV. Fix: leer `Gamepad.current` del Input System nuevo en vez de
`UnityEngine.Input`.

### 5.2. `boss == null` no alcanza para saber quién murió

`TrainingEpisodeManager` originalmente asumía que `boss == null` significa, sin ambigüedad, que el
boss murió (comentario en el código: *"This is a guarantee as with the new SaveState respawn
handling the boss does not go null in between as it did before"*). Con el flujo real de esta
sesión de logging (SaveState + F5 de Debug Mod), morir como Hornet **también** dispara un reload
completo de la escena ("Reloading same scene!") que anula el `HealthManager` del boss — o sea,
`boss == null` pasa igual sin importar quién haya muerto.

Se probó primero cachear el último HP visto del boss antes de anularse, asumiendo que un boss
realmente derrotado mostraría HP cercano a 0. Los datos reales lo refutaron: una victoria real
mostró `last known boss hp = 5` (el remate es una animación scripteada, no siempre agota el HP a
cero), mientras que una derrota real mostró boss hp alto (200-240) porque Hornet murió con el boss
casi intacto. La señal que sí es confiable: **la vida de Hornet en el mismo instante en que
`boss == null`**. Si sigue viva (>0), murió el boss. Si está en 0, murió Hornet. Así quedó
`UpdateEpisodeState`.

### 5.3. `HeroDead` también necesita el ciclo de F5

Relacionado con el bug anterior: como morir Hornet también anula el boss, el reset original para
`HeroDead` (que llamaba a `ResetEpisode()` de una, asumiendo que el juego revivía a Hornet solo
sin reload) dejaba `boss == null` para siempre — nunca se presionaba F5 para recargar la escena, y
el estado volvía a detectarse como "Hornet murió" en cada tick siguiente (loop infinito de log).
Fix: `HeroDead` ahora usa el mismo `HandleDeathReset` (esperar, simular F5, esperar a que el boss
reaparezca) que ya usaba `BossDead`.

### 5.4. Loggear la fila terminal antes de disparar el reset

En la rama de logging de `FixedUpdate()`, si se llama a `HandleResetSequence()` antes de loggear
la fila con el `outcome`, ese método dispara el reset (F5) en el mismo frame y devuelve `true` —
lo que cortaba la ejecución (`return`) antes de llegar al `DatasetLogger.LogRow`/
`currentEpisodeId++`. El outcome nunca se veía en el CSV y el episode_id no avanzaba. Fix: loggear
la fila terminal (con fallback a la última observación cacheada si `Boss` ya es `null`, ver 5.2)
**antes** de llamar a `HandleResetSequence()`.

### 5.5. `.csproj` sin wildcard + orden del `PostBuildEvent`

El `.csproj` no usa wildcard — cada `.cs` se registra a mano en `<ItemGroup>`, así que los archivos
nuevos (`Logging/HumanInputReader.cs`, `Logging/DatasetLogger.cs`) se agregaron explícitamente. El
`PostBuildEvent` que copia el `.dll` a `BepInEx/plugins/` tiene que ir **después** del
`<Import Project="...Microsoft.CSharp.targets" />` final: MSBuild evalúa las propiedades de forma
secuencial en el orden del archivo (a diferencia de los `ItemGroup`, que ven el valor final de
todas las propiedades sin importar su posición), así que un `PostBuildEvent` declarado antes de ese
`Import` ve `$(TargetPath)` vacío y el `$(GameDir)` por defecto del `.csproj` en vez del que
sobreescribe `SilksongRL.csproj.user`.

### 5.6. Locale del sistema y separador decimal del CSV

El sistema corre en español (coma como separador decimal). `DatasetLogger` formatea todos los
floats con `CultureInfo.InvariantCulture` explícitamente — sin esto, el CSV se hubiera roto (comas
de más rompiendo el parseo de columnas).

## 6. Formato del CSV

Ruta: `<Paths.GameRootPath>/SilksongRL_Dataset/lace1_dataset.csv` (append, un archivo para toda la
recolección).

```
episode_id,tick,hero_x,hero_y,hero_vel_x,hero_vel_y,hero_hp,boss_x,boss_y,boss_vel_x,boss_vel_y,
boss_hp,attack_idle,attack_comboslash,attack_counter,attack_rapidslash,attack_jslash,
attack_downstab,attack_charge,attack_evade,attack_crossslash,attack_stun,attack_multihit,
move,look,jump,attack,dash,outcome
```

- 21 columnas de estado (10 de posición/velocidad/HP + 11 one-hot de ataque del boss), todas
  normalizadas a [0,1].
- `move`/`look`: enteros crudos (0/1/2 = None/Left-Up/Right-Down). El one-hot, si hace falta, se
  arma en Python al preparar el dataset de entrenamiento.
- `jump`/`attack`/`dash`: 0/1.
- `tick`: contador que arranca en 0 y se resetea por episodio (no es un frame counter global).
- `outcome`: `-1` en filas intermedias, `0` (murió Hornet) o `1` (murió el boss) solo en la última
  fila de cada intento. Sirve para filtrar episodios completos vs. cortados — no es el atributo
  objetivo del clasificador (el objetivo es la acción).
