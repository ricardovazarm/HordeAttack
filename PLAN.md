# PLAN.md — HordeAttack (POC)

## Objetivo

Juego VR cooperativo tipo *Fight Quest*: hordas de enemigos avanzan hacia los jugadores y estos las repelen a puñetazos. Los enemigos aguantan 2-3 golpes, salen despedidos con fuerza proporcional a la velocidad de la mano, los mandos vibran al conectar, y también se pueden **agarrar con el grip y aventar contra otros enemigos**. La métrica de juego es cuántos enemigos aguantas.

**Alcance de este POC:** dummies sin arte final, 20-30 enemigos simultáneos, 2 jugadores. Ambientes y diseño de enemigos se deciden después.

## Decisiones tomadas (2026-07-24)

| Tema | Decisión |
|---|---|
| Verificación | Quest con Link/Air Link al PC (Play en editor, se ve en el visor) |
| Escala POC | 20-30 enemigos simultáneos |
| Impacto | Knockback proporcional a velocidad de mano + haptics + agarrar y aventar |
| Jugadores | 2, probados con Multiplayer Play Mode |
| Tamaño de enemigo (2026-07-25) | Gnomos/duendes de **1 m**, por debajo de la altura de ojos del jugador |

## Arquitectura elegida

**Los enemigos son NetworkObjects que heredan de `NetworkPhysicsInteractable`.** Esta es la decisión central y se toma porque poder agarrarlos exige transferencia de propiedad de red, que es justo lo que esa clase ya implementa. Nos regala, sin escribir código:

- Transferencia de propiedad al agarrar (`OnSelectEnteredLocal` → `ChangeOwnership`), con desactivación temporal del `ClientNetworkTransform` para que el agarre se sienta instantáneo bajo latencia.
- **Robo de propiedad por colisión** cuando un objeto va rápido (`OnCollisionEnter` → `RequestOwnership()`). Esto es literalmente "aviento un enemigo contra otro" ya resuelto.
- Sincronización de físicas y reseteo de estado.

Se descartó el patrón de *Whack-A-Pig* (enemigos como GameObjects locales instanciados por RPC con semilla compartida, sin NetworkObject). Es más barato en red y sería lo correcto para 100+ enemigos, pero no permite agarrar ni transferir autoridad. A 20-30 enemigos el costo de NetworkObject es asumible; si más adelante se sube la escala, este es el primer punto a revisar.

**Autoridad:** el *session owner* simula la IA y el spawner. El daño es autoritativo en el dueño del enemigo (`[Rpc(SendTo.Owner)]`), pero el golpe se detecta **localmente en el cliente que golpea** y aplica feedback (haptics, VFX) de inmediato sin esperar confirmación. Es el mismo compromiso que usa el template en Whack-A-Pig: latencia percibida cero, reconciliación después.

**Detección de golpe:** umbral de velocidad de mano, no impulso físico puro. Un collider en cada mano del jugador local; al entrar en contacto con un enemigo, si la velocidad suavizada supera el umbral, cuenta como puñetazo y la magnitud de la velocidad escala el daño y el impulso de knockback. La física pura se siente mal en VR porque no hay resistencia háptica real y las manos atraviesan objetos.

**Autoría de escena y prefabs por código.** Editar YAML de Unity a mano es frágil, así que la escena y los prefabs del POC se generan con scripts de editor bajo `Tools > HordeAttack`. Cada fase deja un menú ejecutable, y así la salida siempre es "corre esto y dale Play", no "acomoda 20 cosas en el inspector".

## Riesgos conocidos

- **Ancho de banda:** 30 `ClientNetworkTransform` a 30 Hz en Quest es el principal riesgo de red. Mitigación en Fase 6: bajar tick de sync, precisión media, y no sincronizar enemigos en reposo.
- **Enemigos agarrables ≠ enemigos baratos:** cada enemigo carga Rigidbody + collider + XRGrabInteractable + NetworkObject. Si el perfilado de Fase 6 sale mal, la salida es hacer que solo los enemigos *cercanos* sean agarrables y el resto sea representación ligera.
- **No existen tests en el proyecto.** Fase 0 crea el assembly de tests, porque sin él no se puede cumplir la regla de "toda clase con lógica lleva sus pruebas".

---

# FASES

> Regla: no se avanza de fase hasta que la anterior esté marcada como completada aquí.

## FASE 0 — Andamiaje, tests y escena base

**Estado:** ✅ Completada — verificada en el visor el 2026-07-25

Crear el assembly de tests (no existía ninguno en el proyecto), el generador de escena POC, y verificar que el ciclo Quest+Link funciona de punta a punta.

Estructura creada:

- `Assets/HordeAttack/Runtime/` — `HordeAttack.asmdef` (referencia `VRMP`, XRI, Netcode) y `HordePocLayout`, que centraliza los nombres de los objetos de la arena y la matemática de anillo que reparte posiciones alrededor del jugador (se reutiliza en el spawner de la Fase 3).
- `Assets/HordeAttack/Editor/` — `HordePocSceneBuilder`, menú `Tools > HordeAttack > 1. Generar Escena POC`. La construcción está separada del menú (`PopulateScene`) para que los tests puedan ejercitar el builder real. Los tests construyen **dentro de la escena activa** y destruyen en teardown lo que crearon: Unity se niega a abrir una escena aditiva mientras hay una sin título sin guardar, que es exactamente el estado en que arranca batch mode.
- `Assets/HordeAttack/Tests/EditMode/` — assembly de tests con NUnit.
- `Assets/HordeAttack/Tests/PlayMode/` — assembly aparte para `UgsPreflightTests`, que necesita play mode (ver Fase 4).
- `Assets/HordeAttack/Materials/Fist.mat` — generado por el builder, no commiteado a mano.

La escena contiene: suelo sólido de 20×20 m con la superficie en y=0, luz direccional, el rig `XRMPT_XR_Origin_Setup` del template recentrado en el origen, **3 dummies cápsula de 1 m** con Rigidbody repartidos en anillo a 3 m mirando hacia el centro, y un **puño visible** en cada ancla de mano del rig.

**Por qué hay que recentrar el rig.** El prefab del template no tiene su `XR Origin (XR Rig)` en el origen: lo lleva a **z = −12 dentro del propio prefab** (`XRMPT_XR_Origin_Setup` z=0.42 → `XR Origin (XR Rig)` z=−12). Poner la raíz del prefab en (0,0,0) deja al jugador a 11.58 m detrás, o sea **fuera del suelo**, que va de z=−10 a z=+10. El builder cancela ese desfase moviendo la raíz hasta que el `XROrigin` cae en el centro de la arena, en vez de asumir que raíz y jugador son el mismo punto.

La prueba que existía asertaba sobre la raíz del prefab, así que pasaba en verde con el jugador fuera del mapa. Se sustituyó por aserciones sobre el `XROrigin`: que está en el centro, que cae dentro de los bounds del suelo, y que los dummies quedan a 3 m **del jugador** (no del origen del mundo).

**Tamaño de los enemigos.** Son **gnomos: 1 m de alto** (`k_DummyHeight`), la mitad de la cápsula por defecto de Unity y por debajo de la altura de ojos del jugador. Es una decisión de diseño, no cosmética — una horda de adultos se lee como multitud, una de criaturas bajas se lee como enjambre, que es lo que se busca. Masa 20 kg para que un golpe sólido los lance de verdad; la curva de knockback se afina en la Fase 1.

**Por qué el POC trae su propio puño.** En la primera prueba en el visor solo se veían los rayos de los interactores: las manos eran invisibles. La causa no era la escena sino el template — el `MeshRenderer` de `ControllerCombined` (dentro de `XRControllerLeftModel` / `XRControllerRightModel`) referencia un material con GUID `be3083a5f26d4e859d594ecbe632f87e` que **no existe en ninguna parte del proyecto**. Un material nulo hace que Unity se salte el dibujado en silencio, sin log ni error, así que la malla estaba ahí pero no se pintaba nunca. Afecta igual a `SampleScene`, no es algo que introdujera el generador.

Se optó por poner un puño propio (esfera de 11 cm) en vez de parchear el YAML de un prefab de terceros: es código bajo test, no toca assets ajenos, y es justo el objeto del que colgará el trigger de golpe en la Fase 1. Se cuelga de las cuatro anclas (`Left/Right Controller` y `Left/Right Hand`) porque `XRInputModalityManager` decide en runtime cuál rama activa según lo que reporte el visor, y eso no se puede saber al generar la escena.

**El puño tiene material propio.** El primer intento reusó `Skin.mat` del avatar del template y salieron **bolas moradas**: ese material está autorado en morado (`_BaseColor` 0.59/0.52/0.76) porque es un placeholder que el sistema de avatares tiñe en runtime con el color del jugador. Fuera de ese contexto nadie lo tiñe. Ahora el builder genera `Assets/HordeAttack/Materials/Fist.mat` (URP/Lit, color piel) la primera vez que hace falta.

Ojo con diagnosticar morado en el visor: puede ser esto o el shader de error, y `material.shader != null` **no** los distingue, porque el shader de error tampoco es nulo. La aserción que sirve es `shader.isSupported`.

**Salida verificable:**
1. Corres `Tools > HordeAttack > 1. Generar Escena POC`.
2. Le das Play con el Quest conectado por Link.
3. Estás **en el centro del suelo**, ves **una esfera color piel en cada mano**, y 3 dummies de 1 m rodeándote a 3 m, todos mirándote.
4. `Window > General > Test Runner` pasa en verde, tanto EditMode como PlayMode.

**Nota:** aún no hay detección de golpes. Si empujas un dummy con el mando no pasa nada — eso llega en la Fase 1. Aquí solo se valida que la escena se genera bien y que se ve correctamente en el visor.

**Ya verificado en automático (2026-07-25):**

- Compila sin errores.
- **34/34 tests en verde**: 31 EditMode + 3 PlayMode (`UgsPreflightTests`). Cobertura de línea combinada **73.6%** (`HordePocLayout` 100%, `HordePocSceneBuilder` 72.6%, `UgsPreflight` 71%; lo no cubierto son el método de menú y las rutas de error cuando falta el prefab, el shader o el `XROrigin`). Corriendo solo EditMode la cifra baja porque `UgsPreflight` queda a 0 — solo lo ejercitan los tests PlayMode.
- **Pruebas de mutación manuales** (cinco, todas detectadas):
  - Bug en `RingPosition` (media circunferencia en vez de completa) → fallan `RingPosition_SpacesPointsEvenly` y `RingPosition_AdvancesClockwiseSeenFromAbove`.
  - Material del puño forzado a `null`, que es exactamente el bug del template → falla `Build_GivesEveryFistARenderableMaterial`, y solo ese.
  - Quitado el `CenterRigOnArena`, que es exactamente el bug del jugador fuera del suelo → fallan los 3 tests de posición y solo esos, con el mensaje `The player starts at (0.00, 0.00, -12.00), which is outside the ground plate spanning (-10.00, -0.20, -10.00) to (10.00, 0.00, 10.00)`.
  - Quitado el escalado de los dummies → fallan `Build_MakesDummiesGnomeSized` (`Dummy_0 is 2.00 m tall instead of 1 m`) y `Build_KeepsDummiesShorterThanThePlayer`, y solo esos.
  - Puños devueltos al material prestado del template → falla `Build_UsesTheDedicatedFistMaterial`, y solo ese.
- La escena está regenerada en `Assets/HordeAttack/Scenes/HordePOC.unity` con el rig del template (GUID verificado), suelo, luz, 3 dummies y 4 puños. Verificado en el YAML: la raíz del rig quedó en z=12, que cancela el −12 interno del prefab, y `Fist.mat` existe con shader URP/Lit y `_BaseColor` 0.85/0.66/0.52.

**Verificado en el visor el 2026-07-25:** jugador en el centro del suelo, puños color piel visibles en ambas manos, 3 gnomos de 1 m alrededor. Fase cerrada.

---

## FASE 1 — Puñetazo local: daño, knockback y haptics

**Estado:** ⬜ Pendiente

Todavía sin red y sin IA. Un dummy quieto que recibe golpes.

- `HandVelocityTracker`: velocidad suavizada de cada mano (ventana móvil, no delta de un frame, que es ruidoso).
- `PunchDetector`: collider de mano + umbral de velocidad → evento de golpe con dirección y magnitud.
- `PunchResolver` (**lógica pura, sin MonoBehaviour**): dada velocidad, umbral y vida actual → daño e impulso. Aislada así justamente para poder testearla de verdad.
- `HordeEnemy` v1: 3 de vida, recibe golpe, sale despedido con impulso proporcional, muere al tercero.
- Haptics vía `SendHapticImpulse` en la mano que conecta, con intensidad escalada por la fuerza.

**Salida verificable:**
1. Play con el visor. Golpeas el dummy: vibra el mando, el dummy sale volando hacia atrás.
2. Golpe suave = sale poco. Golpe fuerte = sale mucho y más lejos.
3. Al tercer golpe el dummy muere.
4. Tests EditMode de `PunchResolver` pasan: umbral (golpe lento no daña), escalado de impulso, y que la vida llega a 0 exactamente al tercer golpe estándar.

---

## FASE 2 — Agarrar y aventar

**Estado:** ⬜ Pendiente

- `XRGrabInteractable` en el enemigo, configurado para que el grip lo levante.
- Enemigo agarrado deja de atacar y entra en estado `Grabbed`.
- Enemigo aventado que impacta a otro: ambos reciben daño en función de la velocidad relativa del impacto.

**Salida verificable:**
1. Con el grip agarras un dummy y lo levantas; se queda colgando de tu mano.
2. Lo avientas contra otro dummy: ambos reciben daño y salen despedidos.
3. Aventarlo contra el suelo suave no lo mata; aventarlo fuerte sí.
4. Tests EditMode del cálculo de daño por impacto entre enemigos.

---

## FASE 3 — Horda: IA, oleadas y pooling

**Estado:** ⬜ Pendiente

- `EnemyLocomotion`: avanza hacia el jugador más cercano, se detiene a distancia de ataque, ataca en intervalos. Sin NavMesh por ahora — dirección directa + separación entre enemigos, que basta en una arena abierta y es mucho más barato.
- Estados: `Walking / Attacking / Staggered / Grabbed / Dead`. Un golpe interrumpe el ataque (stagger).
- `HordeSpawner` con oleadas crecientes, reusando `Pooler` del template en vez de Instantiate/Destroy.
- Contador de enemigos eliminados y oleada actual.

**Salida verificable:**
1. Play: aparecen oleadas, los dummies caminan hacia ti y te atacan.
2. Puedes repelerlos a puñetazos y agarrando/aventando.
3. Con 20-30 enemigos vivos simultáneamente sigue siendo jugable en Link.
4. El contador de oleada y de eliminados sube correctamente.
5. Tests EditMode de la lógica de oleadas (cuántos enemigos por oleada, cuándo avanza) y de la máquina de estados del enemigo.

---

## FASE 4 — Red: enemigos replicados entre 2 jugadores

**Estado:** ⬜ Pendiente

**Preflight obligatorio:** los tests PlayMode `HordeAttack.Tests.UgsPreflightTests` en verde. Comprueban de verdad —inicializan UnityServices, hacen login anónimo y lanzan una consulta de sesiones— porque si un servicio está apagado en el dashboard no hay nada local que lo delate. Sin esto en verde no se empieza la fase: se depuraría código de red creyendo que el problema es el código cuando es un toggle del dashboard.

Tienen que ser PlayMode: Unity lanza `ServicesInitializationException: You are attempting to initialize Unity Services in Edit Mode` si se intenta desde el editor.

**Ejecutado el 2026-07-25 (editor cerrado): 3/3 en verde.**

```
=== Comprobación de servicios UGS ===
  [OK] Proyecto vinculado / UnityServices
  [OK] Authentication (player id AmVH7keYF0cKsaWDch1BtIJjJ8Wy)
  [OK] Multiplayer / Sessions (0 sesiones visibles)
```

- `HordeEnemy` pasa a heredar `NetworkPhysicsInteractable`.
- Spawner y IA corren solo en el *session owner*; manejo de `OnSessionOwnerPromoted` para que si se va, otro cliente retome la simulación.
- Registro de los prefabs de enemigo en el `NetworkPrefabsList`.
- Daño autoritativo en el dueño vía `[Rpc(SendTo.Owner)]`, con feedback local inmediato en quien golpea.

**Salida verificable:**
1. Con Multiplayer Play Mode levantas 2 instancias (una en el visor por Link, otra en pantalla).
2. Ambas ven los mismos enemigos en las mismas posiciones.
3. Si el jugador A golpea un enemigo, el jugador B lo ve salir volando.
4. Si A agarra un enemigo, B lo ve agarrado y B **no** puede agarrarlo.
5. A avienta un enemigo contra otro y B ve ambos impactos.

---

## FASE 5 — Co-op completo y marcador compartido

**Estado:** ⬜ Pendiente

- Vida/estado del jugador y condición de derrota compartida.
- Marcador de oleada y eliminados sincronizado, visible para ambos.
- HUD en el visor.
- Manejo de entrada/salida de jugadores a media partida.

**Salida verificable:**
1. Dos jugadores sobreviven oleadas juntos y ven el mismo número de oleada y el mismo total de eliminados.
2. Si uno se desconecta a media partida, la partida sigue y los enemigos que tenía agarrados no se quedan colgados.
3. Al perder, ambos ven la misma pantalla de resultado con el número máximo de enemigos aguantados.

---

## FASE 6 — Medición de rendimiento en Quest standalone

**Estado:** ⬜ Pendiente

La única fase que exige build real al visor, porque Link enmascara el costo de CPU/GPU del Quest.

- Build Android e instalación por adb.
- Medición de FPS con 10 / 20 / 30 / 40 enemigos.
- Ajuste de tick de red, precisión de sincronización y no-sync de enemigos en reposo.

**Salida verificable:**
1. Build instalado y corriendo en el visor sin cable.
2. Tabla de FPS medidos por número de enemigos, en el visor, con 2 jugadores conectados.
3. Conclusión escrita: cuántos enemigos soporta de verdad el POC y cuál es el cuello de botella.

---

## Registro de avance

Al cerrar cada fase: correr la suite completa + cobertura, marcar la fase como completada aquí y anotar qué quedó probado.

| Fase | Estado | Fecha | Notas |
|---|---|---|---|
| 0 | ✅ | 2026-07-25 | Escena, assemblies y tests listos. 34/34 verde (31 EditMode + 3 PlayMode), cobertura 73.6%, 5 mutaciones detectadas. Corregidos en el visor: manos invisibles, jugador fuera del suelo, puños morados, enemigos del doble de tamaño. Verificada en el visor. |
| 1 | ⬜ | | |
| 2 | ⬜ | | |
| 3 | ⬜ | | |
| 4 | ⬜ | | |
| 5 | ⬜ | | |
| 6 | ⬜ | | |
