# PLAN.md — HordeAttack (POC)

## Objetivo

Juego VR cooperativo de horda. **No es un juego de pelea.** Los enemigos no te golpean: corren hacia ti y **se te cuelgan encima para inmovilizarte**. Unos te brincan al torso, otros se te aferran a piernas, cintura y brazos. Mientras más te tengan agarrado, más rápido pierdes. La referencia es el *Burly Brawl* de **Matrix Reloaded**: decenas de Smiths sepultando a Neo.

Los puñetazos y el agarre **no son el combate: son la forma de quitártelos de encima**. Golpeas a los que se acercan para que no lleguen, y a los que ya te tienen colgados para que se suelten; o los arrancas con el grip y los avientas contra los que vienen, convirtiendo un problema en un arma. Los enemigos aguantan 2-3 golpes, salen despedidos con fuerza proporcional a la velocidad de la mano, y los mandos vibran al conectar.

La métrica de juego es cuánto aguantas sin que te sepulten.

**Alcance de este POC:** dummies sin arte final, 20-30 enemigos simultáneos, 2 jugadores. Ambientes y diseño de enemigos se deciden después.

## Decisiones tomadas (2026-07-24)

| Tema | Decisión |
|---|---|
| Verificación | Quest con Link/Air Link al PC (Play en editor, se ve en el visor) |
| Escala POC | 20-30 enemigos simultáneos |
| Impacto | Knockback proporcional a velocidad de mano + haptics + agarrar y aventar |
| Fuerza del knockback (2026-07-25) | Se afina en **distancia, no en impulso**: el golpe más flojo que cuenta manda al enemigo **5 m** y uno a tope **10 m**. Antes eran ~30 cm y el enemigo se levantaba al lado tuyo |
| Jugadores | 2, probados con Multiplayer Play Mode |
| Tamaño de enemigo (2026-07-25) | Gnomos/duendes de **1 m**, por debajo de la altura de ojos del jugador |
| Arranque de una partida (2026-07-25) | Los enemigos empiezan **por delante del jugador, en abanico, a 7-8.5 m y a distancias desiguales**, y avanzan a **1.6 m/s** (techo de diseño: 3 m/s). Lo que se afina es el **tiempo de llegada, ~4 s**, no la distancia |
| **Qué hace el enemigo (2026-07-25)** | **No golpea: se te cuelga para inmovilizarte.** Referencia: Burly Brawl de Matrix Reloaded |
| Efecto de estar colgado (2026-07-25) | Solo se aferra y **drena vida** (desde Fase 5). **No** bloquea la mano ni frena la locomoción: quitarle el control al jugador se siente a bug |
| Cómo te zafas (2026-07-25) | **Las dos vías:** a puñetazos con la mano libre, o arrancándolo con el grip y aventándolo |
| Dónde se cuelgan (2026-07-25) | Piernas, cintura, brazos y torso/hombros. **Nunca en la cabeza ni tapando la vista** — marea y se lee como bug |
| Formas de llegar (2026-07-25) | Dos estilos mezclados: unos **brincan encima** (anclas altas) y otros **agarran por abajo/los lados** (anclas bajas), llegando desde direcciones distintas |
| Tamaño de la horda en el POC (2026-07-26) | **10 enemigos** en escena en vez de 3, para probar la lectura de horda antes de que exista el spawner. La banda de spawn se ensanchó a **7-9.5 m** porque el escalonado de llegadas no cabe en 1.5 m con 10 (ver Fase 2b) |

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
- **Comodidad al colgarse (Fase 2a).** Es el riesgo nuevo y solo se detecta con el visor puesto: un enemigo aferrado demasiado cerca de la cámara tapa la vista y marea, y uno colgado en un punto que no alcanzas con la otra mano se siente injusto. Mitigación: esfera prohibida alrededor de la cabeza, anclas solo en puntos alcanzables, y prueba en el visor antes de cerrar la fase — ninguna prueba automática detecta "esto marea".

---

# FASES

> Regla: no se avanza de fase hasta que la anterior esté marcada como completada aquí.

## Dónde retomar (2026-07-26)

**Siguiente paso: ponerte el Quest y correr la salida verificable de la Fase 2b.** No hay que escribir código para eso; la escena ya está regenerada y guardada en `Assets/HordeAttack/Scenes/HordePOC.unity`, ahora **con 10 enemigos**. Abrirla y darle Play.

Lo que hay que mirar con atención es lo **nuevo**, que es el arbitraje puño/grip y nunca se ha visto puesto:

1. Aprietas el grip y estiras la mano hacia un dummy suelto: **lo agarras, no sale volando**.
2. Grip apretado y mano quieta: el que camina hacia ti **se te queda en la mano**.
3. Mano abierta: ese mismo movimiento sigue siendo un puñetazo que lo manda 5-10 m.
4. Sueltas el grip y golpeas de inmediato: el golpe conecta.

Si eso se siente bien, se repasa el resto de la lista de la Fase 2b (arrancar un colgado, aventar contra otro, aventar contra el suelo, el cadáver se cae de la mano), se marca la fase ✅ y se arranca la Fase 3.

**Con 10 enemigos hay dos cosas nuevas que mirar de paso**, porque es la primera vez que se prueban a esta escala: si **6 colgados a la vez** (el máximo, hay 6 anclas de cuerpo) tapan la vista o marean, y qué hacen los **4 que se quedan sin ancla** — hoy siguen empujando alrededor sin engancharse, que es una lectura que nunca se ha visto puesta.

**Estado del repo:** todo lo de la Fase 2b y la subida a 10 enemigos está **commiteado y pusheado** a `main`. Los drivers de mutación (`mutate3.py`, `mutate4.py`) vivían en el scratchpad de la sesión y **se perdieron** al limpiar; si hacen falta otra vez, se rehacen desde las tablas de mutaciones de abajo.

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

> Lo de arriba describe la escena tal como quedó al cerrar esta fase. La **Fase 2a la cambió**: el suelo pasó a 30×30 m y los dummies salieron del anillo de 3 m para empezar en abanico por delante del jugador, a 7-8.5 m. El resto sigue igual.

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

**Estado:** ✅ Completada — verificada en el visor el 2026-07-25

Todavía sin red y sin IA. Tres dummies quietos que reciben golpes.

Todo lo nuevo vive en `Assets/HordeAttack/Runtime/Combat/`:

| Archivo | Qué es |
|---|---|
| `VelocityWindow.cs` | Lógica pura. Ventana deslizante que estima la velocidad de un punto. |
| `PunchSettings.cs` | Datos serializables del modelo de golpe (umbral, daño, knockback, haptics). |
| `PunchResolver.cs` | Lógica pura. Velocidad de mano + vida actual → `PunchOutcome` (daño, impulso, vibración). |
| `HandSide.cs` | Enum izquierda/derecha. |
| `HandVelocityTracker.cs` | MonoBehaviour delgado que alimenta el `VelocityWindow` desde `LateUpdate`. |
| `PunchDetector.cs` | MonoBehaviour en el puño: trigger + tracker + resolver + vibración. |
| `HordeEnemy.cs` | MonoBehaviour: vida, knockback, destello, muerte y reaparición. |

### Decisiones y por qué

**La velocidad se estima con mínimos cuadrados, no con el delta del último frame ni con la resta de los extremos de la ventana.** El delta de un frame es inservible: un frame de tracking perdido o mal predicho mueve la mano 20 cm de golpe, lo que se lee como ~14 m/s y dispara un puñetazo a máxima potencia que el jugador nunca tiró. Restar el primer y el último punto de la ventana arregla el caso del pico *en medio*, pero no el del pico *en la última muestra*, porque ahí esa muestra se lleva todo el peso. El ajuste por mínimos cuadrados le da el peso de una muestra entre N. Es lo mismo que hace el `AttachPointVelocityTracker` de XRI. Ventana de 0.09 s, definida en segundos y no en número de muestras para que signifique lo mismo a 72 Hz en standalone que a la tasa que dé el editor por Link.

**Curva de golpe.** Umbral 1.5 m/s (por debajo es un roce), potencia máxima a 7.5 m/s. El daño es `ceil(potencia × 2)` acotado a [1, 2]: cualquier golpe que cuente hace al menos 1 (un golpe que conecta y no hace nada se siente roto), y por encima de media potencia —4.5 m/s— hace 2. Con 3 de vida eso da los **2-3 golpes** que pide el objetivo. El impulso es `min(velocidad, 7.5) × 15 N·s`, o sea proporcional a la velocidad pero con el mismo techo que el daño, para que un manotazo descontrolado no mande a nadie a la órbita. A 20 kg de enemigo, un golpe estándar de 3 m/s da 45 N·s ≈ 2.25 m/s de knockback, y uno a tope 5.6 m/s.

> **El modelo de knockback de este párrafo ya no es el vigente.** Al probarlo en el visor resultó que esos 2.25 m/s con 19° de elevación son **30 cm de alcance**: el enemigo caía al lado del jugador y se le volvía a echar encima. La **Fase 2a lo reemplazó** por un modelo afinado en distancia (5-10 m). El daño, el umbral y la curva de potencia siguen tal cual.

**El knockback lleva un sesgo hacia arriba (0.35) y se orienta por la componente horizontal del swing.** Sin el sesgo el enemigo resbala por el suelo, que se lee como empujón y no como golpe. Usar la horizontal en vez de la velocidad cruda evita que un golpe tirado hacia abajo clave al enemigo contra el suelo, donde el collider simplemente se come el impulso.

**El puño es un trigger, no un collider sólido**, con Rigidbody cinemático. Sólido, la física empujaría a los enemigos en cada roce y el knockback dejaría de venir del modelo de golpe. El Rigidbody cinemático es lo que hace que los eventos de trigger lleguen de forma fiable: sin él Unity trata al collider en movimiento como un cuerpo estático que se teletransporta. El radio del trigger (9 cm) es mayor que el puño visible (5.5 cm) a propósito: en VR la mano no encuentra resistencia, así que cuando el puño visible ya está *dentro* del enemigo el jugador siente que pasó de largo.

**Se resuelve en `OnTriggerEnter` y también en `OnTriggerStay`, con cooldown de 0.35 s por enemigo.** Solo con enter, un jugador que conecta y deja la mano metida no vuelve a golpear jamás. Solo con stay y sin cooldown, el golpe haría daño en cada paso de física. El cooldown **solo se arma cuando el golpe cuenta**: si se armara también con los roces, un roce lento bloquearía el puñetazo real que llega una décima después.

**Haptics con `HapticsUtility.SendHapticImpulse`** (`UnityEngine.XR.Interaction.Toolkit.Inputs.Haptics`), estático, sin componente. Se revisó la alternativa: el rig del template **no tiene ningún `HapticImpulsePlayer` autorado** en ninguno de los dos mandos, y el que XRI crea solo cuando hace falta se construye por una API `internal` que este assembly no puede llamar. El rig sí trae `ActionBasedController` con la acción háptica enlazada, pero está marcado `[Obsolete]`. La utilidad estática solo necesita saber la mano, y por eso el generador de escena **hornea la lateralidad** en cada puño (`HordePocLayout.HandSideOf`): no se puede deducir del transform en runtime, y un puño que vibra el mando equivocado es un bug que solo se ve con el visor puesto.

**Los enemigos se tiñen con `MaterialPropertyBlock`**, no asignando `Renderer.material`. Asignar el material lo clona por enemigo: a los 20-30 enemigos del objetivo serían 30 materiales que ya no batchean. Hay una prueba que verifica que el shader del dummy declara `_BaseColor`, porque escribir una propiedad que el shader no tiene es un no-op **silencioso** y el destello de impacto simplemente no se vería.

**Los enemigos reaparecen** (1.5 s de cadáver + 3 s). El POC tiene 3 dummies fijos y no hay spawner hasta la Fase 3; sin esto, la única forma de volver a probar el tercer golpe es salir y volver a entrar en Play. Se ocultan apagando renderers y colliders, **no desactivando el GameObject**: un objeto desactivado detiene sus propias corrutinas y el temporizador de reaparición nunca dispararía. La Fase 3 reutiliza `Respawn()` como paso de reciclado del pool.

### Salida verificable (verificada en el visor el 2026-07-25)

1. `Tools > HordeAttack > 1. Generar Escena POC` — **ya está regenerada y guardada**, pero vuelve a correrlo si tocas el builder.
2. Play con el Quest por Link. Golpeas un dummy: **vibra el mando**, el dummy **destella en blanco** y sale volando hacia atrás y hacia arriba.
3. Golpe suave = sale poco. Golpe fuerte = sale mucho y más lejos. Por debajo de 1.5 m/s de mano no pasa nada (puedes empujarlo despacio y comprobarlo).
4. Al tercer golpe estándar el dummy se pone **rojo oscuro**, sale despedido, desaparece a los 1.5 s y reaparece 3 s después en su sitio.
5. `Window > General > Test Runner` en verde, EditMode y PlayMode.

### Ya verificado en automático (2026-07-25, editor cerrado)

- Compila sin errores.
- **99/99 tests en verde**: 80 EditMode + 19 PlayMode. Cobertura de línea combinada **86.2 %** (subió desde el 73.6 % de la Fase 0). Por clase: `HordePocLayout` 100 %, `PunchSettings` 100 %, `PunchOutcome` 100 %, `HordeEnemy` 97 %, `VelocityWindow` 94.7 %, `PunchResolver` 91.6 %, `HandVelocityTracker` 91.6 %, `HordePocSceneBuilder` 75 %, `PunchDetector` 75 %, `UgsPreflight` 71 %.
- Lo no cubierto de `PunchDetector` es la rama de haptics —**no hay mando conectado en batch mode**, así que la vibración real solo se puede comprobar en el visor; lo que sí está cubierto es la amplitud y duración que el resolver calcula— más `PruneExpired`, `OnDisable` y `OnValidate`.
- Los tests PlayMode existen porque `Awake` **nunca corre** en un componente añadido en EditMode (el enemigo se estaría probando sin inicializar la vida) y porque `AddForce` no aparece en la velocidad hasta que la física simula. Ojo: un solo `WaitForFixedUpdate` **no basta**, resume antes del paso de física; hay que esperar dos.
- **Pruebas de mutación manuales** (ocho, todas detectadas, cada una por los tests que le tocan):

  | Mutación | Falla |
  |---|---|
  | Quitar el umbral de velocidad (EditMode) | `Resolve_IgnoresAHandThatIsBarelyMoving`, y solo ese |
  | Quitar el umbral de velocidad (PlayMode) | `Swing_TooSlowly_LandsNothingAtAll`, `ReceivePunch_IgnoresASwingBelowTheThreshold`, `RestingInsideAnEnemy_DoesNotKeepDealingDamage`, `ReceivePunch_RaisesOnPunchedForEveryPunchThatLands` |
  | Quitar el sesgo hacia arriba del knockback | `Resolve_LiftsTheEnemyOffTheFloor`, `Resolve_DoesNotDriveTheEnemyIntoTheGroundOnADownwardSwing` |
  | Quitar el tope del impulso | `Resolve_CapsKnockbackAtFullPowerSoAFlailCannotLaunchAnyone`, y solo ese |
  | Sustituir mínimos cuadrados por delta del último frame | `Velocity_DampsATrackingGlitchOnTheNewestSample`, y solo ese |
  | Romper `HandSideOf` para la mano izquierda | los 2 tests de `HandSideOf` + los 27 del builder (lanza durante la construcción) |
  | Quitar el cooldown de re-golpe | `Swing_ThroughAnEnemy_LandsExactlyOnePunch`, `Swing_TwiceInARow_LandsTwoPunches` |
  | Que el enemigo no aplique el impulso | `ReceivePunch_ActuallyThrowsTheEnemy`, `ReceivePunch_ThrowsAHardPunchFurtherThanASoftOne` |

- Escena regenerada y guardada en `Assets/HordeAttack/Scenes/HordePOC.unity`. Verificado en el YAML: 4 puños con `PunchDetector` + `HandVelocityTracker` + trigger, con `m_Hand` 0/0/1/1 (dos izquierdas, dos derechas), y 3 dummies con `HordeEnemy`.

### Qué NO se hizo y por qué

- **Nada de red.** `HordeEnemy` sigue siendo `MonoBehaviour`; pasa a `NetworkPhysicsInteractable` en la Fase 4. Por eso toda la matemática vive en `PunchResolver` y no dentro del componente: mover la mutación de vida detrás de un `[Rpc(SendTo.Owner)]` tiene que ser un cambio chico.
- **No se puede agarrar** todavía (Fase 2b) ni hay IA (Fase 2a).
- La vibración real **no está probada automáticamente**: en batch mode no hay dispositivo XR. `HapticsUtility` devuelve `false` en silencio, sin excepción, así que los tests corren igual pero no prueban ese eslabón.

---

## FASE 2a — Que se te echen encima, y quitártelos a golpes

**Estado:** ✅ Completada — verificada en el visor el 2026-07-25

Esta es la fase que convierte la escena en un juego: hasta ahora los dummies eran sacos de box quietos. Aquí **te buscan, se te cuelgan y te los quitas a puñetazos**.

**Por qué se partió la Fase 2 en dos.** Colgarse y arrancar con el grip son dos mecánicas independientes que se pueden probar por separado, y juntas hacían una fase demasiado grande para una sola sesión de visor. Además el orden importa: si al probar la 2a resulta que la distancia de acercamiento marea o que los puntos de anclaje no se alcanzan, eso hay que arreglarlo **antes** de construir el arrancar-con-grip encima. Al revés se tira trabajo.

**Todavía no te quitan vida.** El objetivo es validar que *defenderse se siente bien*: que ves venir al enemigo, que entiendes que te agarró, y que quitártelo es satisfactorio. La vida del jugador y la derrota llegan en la Fase 5; meterlas aquí solo agregaría una variable más que ajustar antes de saber si el gesto base funciona.

Sigue sin haber red (Fase 4) ni oleadas (Fase 3): **los mismos 3 dummies** de la Fase 1, con el `Respawn()` que ya existe devolviéndolos al ataque.

> Al final de la Fase 2b esos 3 pasaron a **10**, y la banda de spawn de 7-8.5 m a **7-9.5 m**. Todo lo que sigue en esta fase describe el estado en que se cerró.

### Alcance

**1. Locomoción — se movió aquí desde la Fase 3.** Sin esto no hay nada que probar: un enemigo que no se te acerca no se te puede colgar. `EnemyLocomotion` avanza hacia el jugador más cercano con dirección directa + separación entre enemigos, sin NavMesh — en una arena abierta y plana basta, y es mucho más barato. La Fase 3 se queda con lo suyo: oleadas, pooling y contadores.

**1b. De dónde arrancan y a qué velocidad** (corregido el 2026-07-25 tras la primera prueba en el visor). Empezaban a 3 m y a 1.6 m/s, o sea que el primero te tenía agarrado en poco más de un segundo: el juego arrancaba contigo ya atrapado. Ahora salen **en abanico por delante del jugador, entre 7 y 8.5 m, cada uno a una distancia distinta**, y avanzan a **1.6 m/s**, con un techo de diseño de **3 m/s**. El primero te alcanza a los **~3.7 s** y el último a los **~4.8 s**.

- **Delante, no alrededor.** La lectura de apertura tiene que ser "vienen por mí", no "algo me agarró por detrás antes de entender que el juego había empezado". Rodear al jugador es de la Fase 3, cuando ya haya oleadas y el jugador haya aprendido a girarse.
- **Distancias desiguales**, repartidas con una secuencia de razón áurea en vez de un generador aleatorio: nunca se repiten ni se agrupan, pero la escena se construye **igual todas las veces**, que es lo que evita bugs que solo aparecen en algunas ejecuciones. Con distancias iguales llegarían en formación.
- **La arena creció de 20×20 a 30×30 m.** Hizo falta cuando el spawn estaba a 12 m, porque el suelo viejo llegaba a 10 m y un enemigo aparecía en el aire. Con el spawn ya a 7-8.5 m sobraría, pero se deja: da sitio para que un enemigo bien golpeado salga volando sin caerse del mapa.
- **Velocidad de avance: 1.6 m/s.** Se probó a 3 m/s y se volvió a 1.6, y el spawn se acercó de 10-12 m a 7-8.5 m para compensar (a 1.6 m/s desde 11 m la partida abría con casi 6 s de espera): la horda amenaza porque no para de venir y hay más de ella que manos tienes, no porque una criatura suelta corra mucho. Los **3 m/s se quedan como techo de diseño** (`k_MaxMoveSpeed`), no como el valor en uso, y están clampeados en `EnemyLocomotionSettings.Clamp()` — un tope que solo vive en un tooltip es un tope que alguien sube afinando otra cosa.
- **El límite real no es la distancia ni la velocidad, es el tiempo.** Cualquiera de los dos números por separado parece inofensivo, y doblar la velocidad deshace haber alejado el spawn. `SpawnBand_GivesThePlayerSecondsToSeeTheHordeComing` prueba las dos caras: **al menos 3 s a la velocidad que los enemigos usan de verdad** —así que también salta si alguien los acelera— y **al menos 2 s aun al techo de 3 m/s**, que es el suelo absoluto por debajo del cual no hay reacción posible.

**2. Dos formas de llegar, mezcladas.** Es lo que hace que se lea como enjambre y no como fila:

- **Saltadores:** a cierta distancia **te brincan encima** y se aferran arriba (torso/hombros).
- **Aferradores:** llegan caminando y se agarran abajo o de lado (piernas, cintura, brazos).

Llegan desde direcciones distintas porque la separación entre enemigos los reparte alrededor, no porque haya un guion.

**3. Puntos de anclaje en el jugador.** Un conjunto de `Transform` colgados del rig (piernas izq/der, cintura, brazos izq/der, torso/hombros), **uno por enemigo**. Sin exclusividad, tres enemigos se meten en el mismo punto y se ven como un solo bulto con z-fighting. El enemigo elige el ancla libre más compatible con su estilo y con la dirección desde la que llegó.

**Nada de anclas en la cabeza, y hard clamp de una esfera prohibida alrededor de la cámara.** Un enemigo tapándote la vista en VR no da miedo: se lee como bug y marea. Los gnomos de 1 m ayudan solos, porque colgados quedan por debajo de la línea de ojos.

**4. Estado colgado.** El enemigo pasa a `Latched`, deja de moverse por su cuenta y queda cinemático, emparentado al ancla. En esta fase **no drena vida**; solo se aferra. Feedback al enganchar: **vibración en ambos mandos + destello rojo breve** — que es exactamente el enganche que la Fase 5 conecta al drenaje real.

**5. Zafarse a puñetazos.** Con la mano libre lo golpeas: se suelta y sale despedido. La vía del grip es la Fase 2b.

### Decisiones técnicas que ya se ven venir

**Golpear a un colgado exige velocidad RELATIVA, no absoluta.** `PunchResolver` hoy recibe la velocidad de la mano en el mundo (`PunchDetector` → `HandVelocityTracker`). Un enemigo colgado se mueve *contigo*: si caminas o giras, tu puño y el enemigo comparten ese movimiento y la velocidad absoluta contaría puñetazos que nunca tiraste. Al revés también: si el enemigo cuelga del brazo izquierdo y mueves ese brazo mientras golpeas con el derecho, lo que importa es la velocidad de cierre entre ambos. Para blancos colgados hay que restar la velocidad del blanco. Es un cambio real al camino de la Fase 1 y lleva su prueba.

**El puño no debe poder golpear al enemigo colgado de su propio brazo.** El trigger vive en la mano; un enemigo aferrado a ese brazo está permanentemente dentro o al borde de ese trigger, así que cada movimiento del brazo resolvería un golpe. Se excluye explícitamente — el cooldown de 0.35 s no basta, solo espaciaría el problema.

**El colgado es cinemático y `ReceivePunch` se protege con `!m_Body.isKinematic`** (`HordeEnemy.cs:122`), así que el impulso se lo tragaría. Hay que devolverlo a dinámico **antes** de aplicar el knockback al soltarse, no después.

**Morir colgado tiene que despegarlo.** `DeathRoutine` apaga colliders y vuelve cinemático el cuerpo (`HordeEnemy.cs:181`); si además sigue emparentado al jugador, el cadáver se va contigo. `Respawn()` también tiene que limpiar el ancla.

**El estado se queda chico a propósito.** Aquí solo hacen falta `Walking / Leaping / Latched / Dead`; `Grabbed` entra en la 2b y la máquina completa con `Staggered` es de la Fase 3, cuando haya oleadas que la justifiquen.

### Salida verificable (verificada en el visor el 2026-07-25)

1. La escena **ya está regenerada y guardada**; abre `Assets/HordeAttack/Scenes/HordePOC.unity` y dale Play con el Quest por Link. Vuelve a correr `Tools > HordeAttack > 1. Generar Escena POC` solo si tocas el builder.
2. Al arrancar, los 3 dummies están **por delante de ti, entre 7 y 8.5 m, cada uno a una distancia distinta** y ya mirándote. **Caminan hacia ti** a 1.6 m/s: el primero te alcanza a los **~3.7 s** y el último a los **~4.8 s**. Unos te **brincan encima** y otros se te **aferran a piernas, cintura o brazos**.
3. Cuando uno se engancha: **vibran los mandos y la vista destella en rojo**. El enemigo se queda colgado y visible, **nunca tapándote la cara**.
4. Le pegas con la mano libre y **se suelta y sale despedido varios metros** — 5 m con un golpe flojo, hasta 10 con uno a tope — así que te da tiempo de ocuparte de otro antes de que vuelva.
5. Estando quieto y solo moviendo el cuerpo o los brazos, **no** se resuelven puñetazos falsos sobre el que ya tienes colgado.
6. Dos enemigos **no se cuelgan del mismo punto**.
7. Al morir colgado, el cadáver **se despega** y no te sigue. Al reaparecer, vuelve a buscarte.
8. `Window > General > Test Runner` en verde, EditMode y PlayMode.

### Qué se construyó

Lo nuevo vive en `Assets/HordeAttack/Runtime/Horde/`:

| Archivo | Qué es |
|---|---|
| `EnemyState.cs`, `LatchStyle.cs`, `LatchHeight.cs` | Enums: estado del enemigo, cómo llega (saltador/aferrador), banda del cuerpo. |
| `HordeSteering.cs` | Lógica pura. Seek + separación, banda de salto y arco del salto. |
| `LatchAnchorSelector.cs` | Lógica pura. Elige el ancla: exclusividad, banda por estilo, lado de llegada, esfera prohibida de la cabeza. |
| `EnemyLocomotionSettings.cs` | Datos serializables del movimiento y del salto. |
| `LatchAnchor.cs` | Un punto agarrable del jugador, con su ocupante. |
| `PlayerBodyProxy.cs` | Torso derivado de la cámara (solo yaw) que coloca sus anclas según la altura de ojos real. |
| `PlayerLatchTarget.cs` | El jugador visto por la horda: registro, reserva/enganche/liberación y eventos. |
| `EnemyLocomotion.cs` | Camina, salta y se engancha. |
| `LatchFeedback.cs` | Vibra ambos mandos y dispara la viñeta roja. |

Modificados: `HordeEnemy` (estado, `AttachTo`/`Detach`, despegue al morir/reaparecer/recibir golpe, registro estático de vivos), `PunchDetector` (velocidad relativa + exclusión del propio brazo), `HordePocLayout` (anclas y nombres), `HordePocSceneBuilder` (cuerpo, anclas y locomoción).

**`HandVelocityTracker` se renombró a `PointVelocityTracker`** (mismo GUID, el archivo se movió con `git mv`). El enemigo necesita medir su propia velocidad para que el puño pueda restarla, y un componente llamado "HandVelocityTracker" colgado de un gnomo es una mentira que se queda para siempre. La clase siempre fue genérica: sigue un `Transform` cualquiera.

### Ya verificado en automático (2026-07-25, editor cerrado)

- Compila sin errores.
- **218/218 tests en verde**: 162 EditMode + 56 PlayMode. Cobertura de línea combinada **89.1 %** (venía de 86.2 %). Por clase: `HordePocLayout`, `LatchAnchorSelector`, `LatchAnchorSlot`, `PointVelocityTracker`, `EnemyLocomotionSettings`, `PunchSettings` y `PunchOutcome` al 100 %; `VelocityWindow` 94.7 %, `HordeSteering` 94.4 %, `HordeEnemy` 94 %, `LatchAnchor` 92.3 %, `LatchFeedback` 91.7 %, `PunchResolver` 91.6 %, `PlayerLatchTarget` 90.9 %, `PlayerBodyProxy` 89.6 %, `EnemyLocomotion` 83.6 %, `HordePocSceneBuilder` 78.7 %, `PunchDetector` 76.9 %, `UgsPreflight` 71 %.
- Lo no cubierto sigue siendo, sobre todo, lo que **no existe en batch mode**: la rama de haptics (no hay mando conectado) y las rutas de error del builder cuando falta el prefab o el shader.
- **Pruebas de mutación manuales: diecinueve, todas detectadas.** El driver está en el scratchpad de la sesión (`mutate.py` y `mutate2.py`); aplica un bug, corre solo la clase de tests que le toca y revierte.

  | Mutación | Falla |
  |---|---|
  | Quitar la esfera prohibida de la cabeza | `Select_RefusesAnAnchorTooCloseToTheHead`, `Select_ReturnsNothingWhenEveryAnchorIsTooCloseToTheHead`, `Select_KeepsWorkingWhenThePlayerCrouches` |
  | Quitar la exclusividad de anclas | los 4 tests de ocupación del selector |
  | Invertir la dirección de llegada | `Select_PrefersTheSideTheEnemyIsArrivingFrom`, y solo ese |
  | Quitar la separación del steering | `Steer_SidestepsRatherThanWalkingThroughANeighbour`, y solo ese |
  | No despegar al colgado antes del knockback | `Punch_KnocksALatchedEnemyOffAndThrowsIt`, `Punch_FreesTheAnchorForTheNextCreature`, `Punch_PutsTheEnemyBackUnderItsOriginalParent` |
  | No soltar al saltador antes del knockback | `Punch_MidLeap_StopsTheJumpAndThrowsTheEnemy`, `Punch_MidLeap_GivesBackTheSpotItWasAimingAt`, y los 2 de recuperación |
  | Medir el golpe en velocidad absoluta | `Walking_WithACreatureHoldingOn_ThrowsNoPunches`, y solo ese |
  | Quitar la exclusión del propio brazo | `PunchingWithAnArm_ThatHasACreatureOnIt_ThrowsNoPunchesAtThatCreature`, y solo ese |
  | No liberar el ancla al desactivar el enemigo | `Disable_FreesTheAnchorEvenThoughNothingCalledDetach` |
  | Quitar la ventana de recuperación tras el golpe | `Knockback_IsNotErasedByTheNextWalkingStep`, `Recovery_EndsAndTheEnemyComesBackForMore`, `Punch_MidLeap_StopsTheJumpAndThrowsTheEnemy` |
  | Devolver el spawn a 3 m | `SpawnBand_GivesThePlayerSecondsToSeeTheHordeComing`, y solo ese |
  | Subir la velocidad de avance a 3 m/s | `SpawnBand_GivesThePlayerSecondsToSeeTheHordeComing`, `Defaults_AdvanceAtAWalkWellInsideTheCeiling` |
  | Subir el techo de velocidad a 6 m/s | `SpawnBand_GivesThePlayerSecondsToSeeTheHordeComing`, y solo ese |
  | Quitar el escalonado de distancias de spawn | `ApproachPosition_GivesEveryEnemyItsOwnDistance`, `Build_StaggersHowFarAwayTheDummiesStart` |
  | Poner el abanico detrás del jugador | los 3 tests del abanico + `Build_StartsEveryDummyInFrontOfThePlayer` |
  | No aplicar el tope de velocidad en `Clamp()` | `Clamp_RefusesToLetAnEnemyCloseFasterThanThePlayerCanReact`, y solo ese |
  | Devolver el knockback a un empujón de 30 cm | `Defaults_ThrowAnEnemyTheDistanceTheDesignCallsFor`, y solo ese |
  | Invertir la fórmula de velocidad de lanzamiento | `LaunchSpeedForRange_ActuallyCoversTheDistanceItIsGiven`, y solo ese |
  | Sumar el knockback en vez de asignarlo | los 3 tests de distancia real + `ReceivePunch_ActuallyThrowsTheEnemy` |

- Escena regenerada y guardada en `Assets/HordeAttack/Scenes/HordePOC.unity`. Verificado en el YAML: 1 `PlayerBodyProxy` + 1 `PlayerLatchTarget` + 1 `LatchFeedback`, **10 `LatchAnchor`** (6 de cuerpo: Chest/Waist/Leg × izq-der, + 4 de brazo, uno por ancla de mano), 3 `EnemyLocomotion` (1 aferrador y 2 saltadores) y 7 `PointVelocityTracker` (4 puños + 3 dummies). Los dummies quedaron en **7.46 m a 35° a la izquierda, 8.39 m al frente y 7.82 m a 35° a la derecha**, a 1.6 m/s, y el suelo en 30×30 m.

### Dos cosas que costaron y no hay que volver a descubrir

**Añadir componentes en runtime no equivale a cargar una escena.** `EnemyLocomotion` declara `[RequireComponent(typeof(HordeEnemy))]`, así que un test que hacía `AddComponent<EnemyLocomotion>()` **antes** que `HordeEnemy` provocaba que Unity creara un `HordeEnemy` propio para satisfacer el require, y el explícito de después quedaba como **segundo** componente: la locomoción enganchaba una instancia y el test miraba la otra, en verde aparente. La forma correcta de construir un objeto por código es **crearlo desactivado, añadir todo y activarlo al final**, que es el único orden en el que cada `Awake` ve el objeto completo, igual que al cargar una escena. Además, todos los componentes nuevos llevan `[DisallowMultipleComponent]`, para que el duplicado sea un error ruidoso y no un fantasma.

**La primera versión del test del propio brazo no probaba nada.** Movía el brazo en línea recta, y ahí el puño y el ancla se desplazan a la misma velocidad, así que la resta de velocidad relativa ya cancelaba el golpe por sí sola: la mutación que quitaba la exclusión seguía en verde. El test se rehízo como una **rotación alrededor del codo**, que es el único movimiento en el que el puño y el ancla —a radios distintos— se separan más rápido que el umbral. Con eso la mutación falla, que es la prueba de que el test sirve.

### Qué NO se hizo y por qué

- **No hay grip.** Agarrar y arrancar es la Fase 2b, con `XRGrabInteractable` e `ImpactResolver`. Hoy la única forma de quitarte un enemigo es a puñetazos.
- **Estar colgado no cuesta nada.** `PlayerLatchTarget.latchedCount` ya publica cuántos te tienen, pero nadie lo lee: el drenaje de vida es de la Fase 5.
- **Nada de red.** `HordeEnemy` sigue siendo `MonoBehaviour`; el reparentado al ancla no se replica y eso se decide en la Fase 4.
- **`Staggered` no existe todavía.** El golpe interrumpe un salto y deja al enemigo a la física durante 0.9 s, pero eso es un temporizador dentro de `Walking`, no un estado propio. Se promueve en la Fase 3, cuando haya oleadas que lo justifiquen.
- **Los enemigos colgados se interpenetran.** Seis anclas sobre un torso humano con criaturas de 1 m no caben sin solaparse. Se deja así a propósito: el amontonamiento es la lectura buscada.
- **La vibración real no está probada automáticamente**: en batch mode no hay dispositivo XR, `HapticsUtility` devuelve `false` en silencio.

### Retoque del knockback (2026-07-25, tras la prueba en el visor)

Al golpearlos salían despedidos ~30 cm: caían al lado del jugador y se le volvían a echar encima de inmediato. El modelo de la Fase 1 escalaba un **impulso** con la velocidad de la mano (15 N·s por m/s), lo que sobre el papel parecía razonable y en balística daba media zancada: un golpe estándar lanzaba a 2.25 m/s con 19° de elevación, o sea 0.3 m de alcance.

**El knockback ahora se afina en metros, que es lo que se ve.** `PunchSettings` declara `minKnockbackDistance` (5 m, el golpe más flojo que cuenta) y `maxKnockbackDistance` (10 m, a potencia máxima), y `PunchResolver.LaunchSpeedForRange` despeja la velocidad de lanzamiento con la fórmula de tiro parabólico, `v = √(alcance·g/sin(2θ))`, tomando θ del `upwardBias` y `g` de `Physics.gravity` (hardcodear 9.81 mentiría en cuanto alguien la cambiara).

Tres consecuencias que conviene tener presentes:

- **`PunchOutcome.impulse` pasó a ser `launchVelocity` (m/s)** y `HordeEnemy` **asigna** la velocidad en vez de sumar un impulso. Sumarla se comía parte del knockback justo del enemigo que venía corriendo hacia ti, o sea el que más merecía salir volando.
- **La masa ya no decide cuánto vuela un enemigo.** Sigue importando para cómo se empujan entre ellos y para los impactos de la Fase 2b, pero la distancia de knockback es ahora independiente del peso, que es lo que convierte los 5 m en una promesa y no en una esperanza.
- **La distancia se prueba con el motor real, no con el modelo.** `ReceivePunch_ThrowsTheEnemyMetersAwayNotCentimetres` levanta un suelo, activa la gravedad, tira el golpe más flojo que cuenta y mide los metros recorridos. Eso es lo que faltaba antes: todos los tests de la Fase 1 asertaban sobre el impulso, así que un golpe de 30 cm pasó la suite entera en verde.

**Y una lección sobre las propias pruebas.** Las aserciones que leen la constante que están validando no valen: bajar `minKnockbackDistance` bajaba también la expectativa y todo seguía verde (la mutación lo demostró). Por eso `Defaults_ThrowAnEnemyTheDistanceTheDesignCallsFor` compara contra un **5 literal**, que es el número acordado: bajarlo tiene que ser una decisión deliberada que alguien tome tocando el test. Es el mismo problema que apareció con la distancia de spawn.

---

## FASE 2b — Arrancar y aventar

**Estado:** 🟡 Implementada y verificada en automático el 2026-07-26 — **falta probarla en el visor**

Se probó una primera vez en el visor el 2026-07-25 y salió un problema de fondo: no se podía agarrar un enemigo suelto porque el mando lo leía como puñetazo. Se corrigió con el arbitraje puño/grip que está más abajo, y esa corrección **todavía no se ha visto en el visor**.

La segunda herramienta de defensa: el grip. Aquí es donde un enemigo colgado deja de ser solo un problema y se vuelve **munición**.

### Qué se construyó

Lo nuevo vive en `Assets/HordeAttack/Runtime/Combat/`:

| Archivo | Qué es |
|---|---|
| `ImpactKind.cs` | Enum: contra otro enemigo o contra el suelo. Cada uno se puntúa en su propia banda de velocidad. |
| `ImpactSettings.cs` | Datos serializables del modelo de impacto: dos bandas de velocidad y el tope de daño. |
| `ImpactResolver.cs` | Lógica pura. Velocidad de acercamiento + qué se golpeó → `ImpactOutcome` (si contó, potencia, daño). |
| `ImpactDetector.cs` | MonoBehaviour en el enemigo: convierte una colisión real en daño para los dos lados. |
| `EnemyGrabInteractable.cs` | `XRGrabInteractable` especializado: despega al enemigo antes de que el toolkit lo tome, y lo devuelve a la física al soltarlo. |

Modificados: `HordeEnemy` (`isHeld`, `holder`, `isGrabbable`, `ReceiveImpact`, `PrepareForGrab`/`ReleaseFromGrab`, `ForceRelease`, `ReleaseToPhysics` compartido con el puñetazo), `EnemyLocomotion` (`ReleaseForGrab` y parar mientras lo sostienen), `PunchDetector` (ignorar también al que traes en esa mano), `EnemyState` (`Grabbed`), `HordePocSceneBuilder` (dummies agarrables + detector de impacto).

### Decisiones y por qué

**Es una subclase de `XRGrabInteractable`, no un componente que escucha `selectEntered`, y eso no es estilo: es lo único que funciona.** El toolkit apunta en `Grab()` las dos cosas que restaurará al soltar — **el padre del transform y si el Rigidbody era cinemático**. Un enemigo colgado del jugador es exactamente las dos: emparentado a un ancla y cinemático. Todos los eventos públicos se disparan **después** de `Grab()`, así que un listener llega tarde y el toolkit ya decidió que al soltarlo lo va a **volver a colgar del pecho del jugador, congelado en el aire**. Sobrescribir `Grab()` es el único gancho que corre antes. Las dos pruebas que lo fijan son `Release_OfATornOffEnemy_DoesNotHangItBackOnThePlayer` (padre y flag cinemático) y `Grab_TearsALatchedEnemyOffThePlayer`.

**No se puede obligar a una mano a soltar mientras el grip sigue apretado.** Cancelar la selección desde el `XRInteractionManager` no pega: el enemigo sigue dentro del trigger de la mano y el toolkit lo devuelve al frame siguiente. Se probaron las dos vías obvias (cancelar, y desregistrar apagando el componente) y las dos se deshacen solas. Lo que el toolkit **sí** respeta es un interactable que dice que no se puede seleccionar, porque lo re-evalúa cada frame. Por eso todo pasa por `HordeEnemy.isGrabbable` (`EnemyGrabInteractable.IsSelectableBy`), que se cierra en dos casos: **muerto** —el cadáver se cae de la mano en vez de pasearse hasta que desaparece— y **recién reaparecido**, durante 0.25 s, para que reciclar un enemigo que alguien tenía agarrado no lo teletransporte a casa y el toolkit lo arrastre de vuelta. Es el mismo truco que hace el `NetworkSocketInteractor` del template al apagar su socket medio segundo tras spawnear.

**Velocity Tracking, no Kinematic.** Es el único `movementType` que deja el Rigidbody dinámico mientras lo cargas, y por tanto el único donde soltarlo le entrega la velocidad de la mano de verdad. Con Kinematic, aventar es dejar caer. Hay una prueba del builder (`Build_MakesEveryDummyGrabbableAndThrowable`) que lo fija, porque es una casilla del inspector que alguien puede cambiar sin darse cuenta de que rompe la fase entera.

**El impacto se mide en la componente NORMAL de la velocidad, no en la magnitud.** No es un refinamiento académico: un enemigo golpeado **derrapa por el suelo un par de segundos, en contacto todo el rato**. Medido en velocidad de cierre cruda, eso es un impacto a máxima potencia en cada paso de física y el enemigo **se muere de resbalar**. Proyectado sobre la normal, ese mismo derrape vale casi cero: solo cuenta la parte del movimiento que lo mete *contra* la superficie, que es lo que significa "impacto". Lo prueban `ApproachSpeed_IgnoresSlidingAlongASurface` (EditMode) y `Skidding_AcrossTheGround_LeavesTheEnemyUnharmed` (PlayMode, con suelo y gravedad reales).

**Dos bandas de velocidad separadas, y esto es lo más delicado de la fase.** Suelo **6→10 m/s**, contra otro enemigo **6→14 m/s**, tope de daño 3 en ambos. Se ven redundantes y no lo son:

- **Contra el suelo el proyectil casi siempre es un enemigo que acabas de golpear.** Sale disparado a hasta 12.5 m/s y vuelve a tocar el piso con una componente vertical de ~4.1 m/s, *cada vez*. Si eso contara, **todo puñetazo mataría** y los "2-3 golpes" de la Fase 1 se irían sin que nadie tocara el modelo de golpe. El umbral de 6 deja 1.9 m/s de margen. `Ground_ShrugsOffTheLandingOfEvenTheHardestPunch` lo asegura leyendo las constantes **del modelo de golpe**, no una copia: subir la distancia de knockback o bajar el umbral del suelo fallan los dos ahí.
- **Contra otro enemigo ese mismo proyectil es justo lo que quieres que duela.** Con el techo del suelo (10), cualquier gnomo golpeado mataría de una a su vecino y el grip sería una forma lenta de hacer lo que ya hacía un jab. Estirar el techo a 14 deja sitio entre un golpe flojo (1 de daño al vecino) y uno a tope (3, muerte). `Creature_TakesDamageFromAnEnemyThatWasMerelyPunchedIntoIt` y `Creature_IsNotKilledOutrightByTheWeakestPunchInTheGame` lo aprietan por los dos lados.

**El daño del impacto es simétrico y se cobra una sola vez.** Unity notifica la misma colisión a los **dos** cuerpos, así que sin nada más el par pagaría el doble. Resuelve solo el enemigo con el `GetInstanceID()` menor —los ids son únicos, así que exactamente uno de los dos resuelve y ninguno tiene que ponerse de acuerdo con el otro sobre quién aventó a quién—. `Throw_IntoAnotherEnemy_HurtsBothOfThemExactlyOnce` asierta **exactamente 1 de daño a cada uno**, no "algo de daño", que es lo que convierte la prueba en una prueba.

**El impacto no aplica knockback propio.** Los dos cuerpos son dinámicos y el motor ya resolvió el choque cuando esto corre; un impulso escrito encima lo duplicaría. La única excepción es un enemigo colgado del jugador: es cinemático en el instante del contacto, así que no recibe impulso y simplemente se cae al soltar el ancla. Que se despegue es lo que importa ahí (`Throw_AtALatchedEnemy_KnocksItOffThePlayer`).

**Un enemigo sostenido no hace daño al chocar, y tampoco lo recibe.** Velocity tracking empuja el cuerpo agarrado a través de lo que sea a la velocidad que pida la mano; contando esos contactos, el jugador segaría la horda **caminando** con un gnomo en el puño, y de paso lo iría desgastando gratis. Hay que **soltarlo** para que sea un arma (`Held_EnemyDrivenThroughAnotherOne_HurtsNeither`).

**El puño ignora también al enemigo que trae esa mano.** Es el mismo problema de la Fase 2a por el otro lado: el interactor que agarra cuelga del mismo transform de mano que el puño, así que el enemigo cargado vive dentro del trigger que lo golpearía. La exclusión es **por mano**, no global: un enemigo en la mano del *otro* jugador sigue siendo blanco legítimo, que es lo que la Fase 4 va a querer.

### Arbitraje puño/grip (2026-07-26, tras la primera prueba en el visor)

Al probarla salió que **no se podía agarrar un enemigo suelto**: el mando lo leía como puñetazo. La única forma de agarrar uno era cuando ya se te había colgado encima.

La causa es que el puño y el grip son **el mismo volumen en el mismo transform de mano** y nadie arbitraba entre ellos: el trigger del puño mide 9 cm y el del Direct Interactor 10 cm, y son hermanos bajo el ancla. Estirar la mano en VR pasa de 1.5 m/s sin querer, así que el puñetazo se resolvía primero y el knockback de 5-10 m se llevaba al enemigo antes de que cerraras los dedos. Un colgado sí se dejaba porque lo tienes pegado al cuerpo y casi no mueves la mano.

**Decisión: mano abierta pega, mano cerrada agarra.** Mientras el grip esté apretado, esa mano no resuelve puñetazos (`PunchDetector.IsGrabbing`).

**Y no basta con eso: había que cambiar también el modo de selección de las manos.** Los cuatro `XRDirectInteractor` del rig del template vienen en `InputTriggerType.StateChange`, donde apretar el grip **sin tener nada agarrado cuenta un solo frame** y deja de contar:

```csharp
case InputTriggerType.StateChange:
    active = wasPerformedThisFrame || (m_HasSelection && !wasCompletedThisFrame);
```

O sea que el jugador que aprieta antes de llegar —que es justo lo que el arbitraje le exige— apagaba su propio puñetazo **y no agarraba nada**: peor que cualquiera de las dos mitades por separado. La mutación lo confirmó: devolver las manos a `StateChange` tumba la prueba cabecera, no solo la del builder. Van juntos, y por eso la elección vive en `HordePocLayout.k_GripTrigger` (= `State`), que leen tanto el builder como los tests. `State` además regala la otra mitad del gesto: cierras la mano, te quedas quieto, y el que viene caminando se te mete en el puño.

**Cómo se lee el grip.** `XRBaseInputInteractor.isSelectActive` **no sirve**: está filtrado por ese mismo `selectActionTrigger` y responde "¿puedo empezar una selección este frame?", que no es la pregunta. Este rig además es de la era XRI 2.x — no serializa `m_SelectInput`, así que el reader queda en `InputSourceMode.Unused` y la entrada real llega por la ruta antigua (`ActionBasedController.selectInteractionState`, con el grip enlazado en `XRI Default Input Actions`). La señal que se rellena igual en **las dos** rutas, cada frame y desde la entrada cruda, es `logicalSelectState.isPerformed`. Es la que se usa, y no toca ninguna API marcada `[Obsolete]`.

Detalles que conviene no volver a descubrir:

- El interactor se busca **hacia abajo desde el ancla de mano**, no hacia arriba desde el puño: son **hermanos**, así que `GetComponentInParent` no lo encuentra nunca. Con `includeInactive`, porque `XRInputModalityManager` apaga la rama que no esté en uso.
- Se busca `XRDirectInteractor` en concreto, no `XRBaseInputInteractor`: bajo el ancla del mando también cuelgan un Ray Interactor y un Teleport Interactor, y el de teleporte lleva su propio controlador con otras acciones — preguntarle por el grip contestaría sobre el joystick.
- La referencia es **opcional a propósito**. Sin interactor en la mano, el puño pega como siempre. Eso es lo que mantiene verdes sin tocarlas las 8 pruebas de `PunchDetectorTests`, que construyen un puño suelto, y lo confirmó la mutación de "suprimir siempre".
- El valor se lee con un frame de retraso (`TryPunch` corre en física, el toolkit refresca el estado en Update). 14 ms contra un gesto de medio segundo.

En los tests la entrada se inyecta por la vía soportada del toolkit (`selectInput.inputSourceMode = ManualValue` + `QueueManualState`), que **solo funciona porque una mano de test no tiene un controlador encima**; con uno, el toolkit toma la ruta antigua e ignora el reader. Ojo: `QueueManualState` surte efecto **al frame siguiente**, por diseño.

### Salida verificable en el visor

1. La escena **ya está regenerada y guardada**; abre `Assets/HordeAttack/Scenes/HordePOC.unity` y dale Play con el Quest por Link. Vuelve a correr `Tools > HordeAttack > 1. Generar Escena POC` solo si tocas el builder.
2. **Aprietas el grip y estiras la mano hacia un dummy suelto: lo agarras, no sale volando.** Con el grip apretado y la mano quieta, el que camina hacia ti **se te queda en la mano**. Con la mano abierta, ese mismo movimiento sigue siendo un puñetazo que lo manda 5-10 m. Sueltas el grip y golpeas de inmediato: el golpe conecta.
3. Con el **grip** agarras un dummy suelto y lo levantas: se queda colgando de tu mano y deja de caminar.
4. Un dummy colgado de ti: lo agarras con el grip y **se lo arrancas de encima**. Al soltarlo **no** vuelve a tu pecho ni se queda congelado en el aire.
5. Lo avientas contra otro dummy: **ambos** reciben daño y salen despedidos. Un empujón flojo no hace nada.
6. Aventarlo **fuerte contra el suelo lo mata**; soltarlo desde la altura de la mano, no. Y un enemigo que golpeaste y sale rodando **no** se muere de rodar.
7. Sosteniendo un enemigo y moviendo el brazo, **no** se resuelven puñetazos sobre el que traes en la mano, ni se le hace daño por pasearlo entre los demás.
8. Al morir con él en la mano, **el cadáver se cae**; no se queda agarrado.
9. `Window > General > Test Runner` en verde, EditMode y PlayMode.

### Ya verificado en automático (2026-07-26, editor cerrado)

- Compila sin errores.
- **263/263 tests en verde**: 186 EditMode + 77 PlayMode (venía de 218 al cerrar la 2a; 258 antes del arbitraje puño/grip). Cobertura de línea combinada **90.4 %** (venía de 89.1 %). Por clase: `ImpactSettings` y `ImpactOutcome` al 100 %, `ImpactDetector` 96.2 %, `ImpactResolver` 94.1 %, `HordeEnemy` 94.8 %, `EnemyLocomotion` 87.9 %, `EnemyGrabInteractable` 84.6 %, `PunchDetector` 81.6 %, `HordePocSceneBuilder` 79.6 %.
- Escena regenerada y guardada. Verificado en el YAML: los 3 dummies llevan `EnemyGrabInteractable` con `m_MovementType: 0` (= `VelocityTracking`) e `ImpactDetector`, y los **4** `XRDirectInteractor` del rig quedaron con `m_SelectActionTrigger: 0` (= `State`) como override de prefab. Ojo al comprobarlo: el campo crudo vive en el prefab del template, así que en la escena hay que buscarlo en `m_Modifications`, no como campo suelto.
- **Pruebas de mutación manuales: dieciséis, todas detectadas**, cada una por los tests que le tocan. Los drivers están en el scratchpad de la sesión (`mutate3.py` para la fase, `mutate4.py` para el arbitraje puño/grip).

  | Mutación | Falla |
  |---|---|
  | No liberar al enemigo antes de que el toolkit tome el control | `Grab_TearsALatchedEnemyOffThePlayer`, `Release_OfATornOffEnemy_DoesNotHangItBackOnThePlayer`, `Held_EnemyNeitherWalksNorTakesHoldOfYou` |
  | No devolverlo a su locomoción al soltarlo | `Release_ThrowsTheEnemyAtTheSpeedOfTheHand`, y solo ese |
  | Dejar que el toolkit agarre un cadáver | `Dying_WhileHeld_MakesTheHandLetGo`, `Respawn_WhileHeld_MakesTheHandLetGo` |
  | Que el enemigo siga caminando mientras lo sostienes | `Held_EnemyNeitherWalksNorTakesHoldOfYou`, y solo ese |
  | Quitar la exclusión del enemigo que traes en la mano | `PunchingWithTheHandThatIsHoldingAnEnemy_ThrowsNoPunchesAtIt`, y solo ese |
  | Cobrar el impacto en los dos cuerpos que Unity notifica | `Throw_IntoAnotherEnemy_HurtsBothOfThemExactlyOnce`, y solo ese |
  | Dejar que un enemigo sostenido haga daño al chocar | `Held_EnemyDrivenThroughAnotherOne_HurtsNeither`, y solo ese |
  | Medir el impacto con la velocidad cruda y no la normal | `ApproachSpeed_IgnoresSlidingAlongASurface`, `Skidding_AcrossTheGround_LeavesTheEnemyUnharmed` |
  | Bajar el umbral del suelo por debajo de la caída de un golpe | `Ground_ShrugsOffTheLandingOfEvenTheHardestPunch`, `Ground_ShrugsOffBeingDroppedFromHandHeight`, `Dropping_OntoTheGround_LeavesTheEnemyUnharmed` |
  | Puntuar el suelo con la misma curva que a otro enemigo | `Defaults_LetAHardSlamAtTheFloorKillAHealthyEnemy`, `Defaults_ScoreTheGroundAndACreatureOnDifferentCurves` |
  | Dejar que un enemigo sostenido se cuelgue del jugador | `Held_EnemyRefusesAnAnchorEvenWhenHandedOne`, y solo ese |
  | No cerrar el agarre al reaparecer | `Respawn_WhileHeld_MakesTheHandLetGo`, y solo ese |
  | Quitar el guard de grip en `TryPunch` | `ReachingForALooseEnemy_WithTheGripHeld_GrabsItInsteadOfPunchingIt`, y solo ese |
  | Devolver las manos a `StateChange` | `Build_LeavesTheGripOpenWhileItIsHeld`, `Grip_HeldBeforeContact_CatchesAnEnemyThatWalksIntoTheHand`, `ReachingForALooseEnemy_WithTheGripHeld_GrabsItInsteadOfPunchingIt` |
  | Suprimir el puñetazo siempre, sin mirar el grip | `ReachingForALooseEnemy_WithTheGripOpen_StillPunchesIt`, `Grip_ReleasedAfterCarrying_LetsTheHandPunchAgain` |
  | No encontrar nunca el interactor de la mano | `ReachingForALooseEnemy_WithTheGripHeld_GrabsItInsteadOfPunchingIt`, y solo ese |

### Tres cosas que costaron y no hay que volver a descubrir

**En batch mode los frames duran 0.2 ms y eso rompe el lanzamiento — pero no es un bug del juego.** El suavizado de lanzamiento de XRI se niega a medir nada en un frame de menos de **1 ms** (`k_DeltaTimeThreshold`, una protección contra un bug de timing del Quest). Una corrida headless no tiene nada que dibujar y despacha frames en ~0.2 ms, así que **todas** las muestras se tiran y todo lanzamiento sale a 0 m/s. Se midió antes de tocar nada (`Time.deltaTime` medio de 0.000212 s). La solución es fijar el reloj con `Time.captureDeltaTime = 1/72` en el `SetUp` de `EnemyGrabTests` —y restaurarlo en el `TearDown`, o se lo lleva el resto de la corrida—. Ojo con el efecto secundario: con el reloj fijado, `Time.deltaTime` y `Time.unscaledDeltaTime` **dejan de coincidir**, así que mover la mano con el reloj unscaled la hace recorrer una fracción de lo que el toolkit luego le acredita, y el lanzamiento sale a un veinteavo de su velocidad. Los helpers de ese archivo usan `Time.deltaTime` a propósito.

**El orden de `AddComponent` con `[RequireComponent]` seguía mordiendo, y esta vez en el builder.** `EnemyLocomotion` declara `[RequireComponent(typeof(HordeEnemy))]`, así que añadirla primero hace que Unity cree un `HordeEnemy` por su cuenta; el `AddComponent<HordeEnemy>()` explícito de después es **rechazado** —`HordeEnemy` es `[DisallowMultipleComponent]`— y **devuelve null**. El builder venía funcionando por accidente desde la Fase 2a: ignoraba el valor de retorno y se quedaba con el que Unity había creado solo. Un test que sí usa el retorno se estrella con un `NullReferenceException` que no dice nada. Corregido: en el builder y en los tests, **`HordeEnemy` va antes que `EnemyLocomotion`**.

**Aventar contra otro enemigo lo prueba `ImpactResolverTests`, no la simulación.** El acoplamiento importante de esta fase —que aterrizar tras un puñetazo no puede ser un impacto letal— se afirma en EditMode como una **desigualdad entre las dos tablas de tuning**, leyendo `PunchSettings` de verdad. Escribirlo como una prueba de física ("golpea y mira si muere") lo habría dejado a merced de que la simulación reprodujera justo el caso peor; escrito como desigualdad, falla el día que alguien suba la distancia de knockback.

### Subida a 10 enemigos (2026-07-26)

El POC llevaba **3 dummies** desde la Fase 0, que alcanzaban para probar mecánicas sueltas pero no para ver si esto se lee como horda. Se subió a **10** (`HordePocSceneBuilder.k_ReferenceDummyCount`). Sigue sin haber spawner —eso es la Fase 3—: son 10 objetos fijos en la escena, con el `Respawn()` de siempre devolviéndolos al ataque.

**Y hubo que ensanchar la banda de spawn de 7-8.5 m a 7-9.5 m**, que es el detalle que no era obvio. El escalonado de distancias sale de una secuencia de razón áurea, y esa secuencia deja su par más cercano a **1/18 de la banda** cuando hay 10 elementos: sobre 1.5 m son 8.4 cm, o sea dos enemigos entrando prácticamente hombro con hombro. `Build_StaggersHowFarAwayTheDummiesStart` exige más de 10 cm entre cualesquiera dos, y falló en cuanto se subió el número — la prueba de la Fase 2a hizo exactamente su trabajo. Con 2.5 m de banda ese par vuelve a **14 cm**.

Se ensanchó por el **borde lejano**, no por el cercano: `k_SpawnNearDistance` está afinado como tiempo de llegada (~3.7 s al primero) y moverlo cambiaría la apertura de la partida. Lo único que cambia es que la cola de la oleada llega algo más tarde, que con una horda es lo que se busca. La arena (radio 15 m) sigue sobrada, y `SpawnBand_GivesThePlayerSecondsToSeeTheHordeComing` cubre el otro lado: si alguien acerca el spawn o acelera a los enemigos, salta.

**Lo que esto NO resuelve:** hay **6 anclas de cuerpo**, así que solo 6 enemigos pueden estar colgados a la vez. Del séptimo en adelante, `LatchAnchorSelector` no encuentra sitio y se quedan empujando alrededor. Se deja así a propósito hasta ver en el visor si 6 encima ya es demasiado; si hicieran falta más, es añadir anclas, no tocar el selector.

Verificado tras el cambio (2026-07-26, editor cerrado): **263/263 en verde** (186 EditMode + 77 PlayMode), cobertura de línea combinada **90.4 %** — las mismas cifras que al cerrar el grip, porque esto no añadió código, solo movió dos constantes. La escena está regenerada y guardada con los 10 dummies entre **7.04 y 9.32 m**, ninguno a menos de 14 cm de otro en distancia.

> Ojo con la cobertura: corriendo **solo EditMode** sale **42.4 %**, no 90.4 %. No es una regresión — es que la mitad PlayMode del proyecto (todo lo que necesita `Awake`, física real o el toolkit) queda a cero. Para el número combinado hay que correr las dos plataformas contra el **mismo** `-coverageResultsPath`.

### Qué NO se hizo y por qué

- **Nada de red.** `HordeEnemy` sigue siendo `MonoBehaviour`. El `ImpactResolver` está fuera del componente justo para que la Fase 4 solo tenga que meterlo detrás de un `[Rpc(SendTo.Owner)]`. El robo de propiedad por colisión de `NetworkPhysicsInteractable` es de la Fase 4; aquí el impacto es local.
- **Con un enemigo en la mano no se puede golpear a otros con él.** Es deliberado (ver arriba). Si al probarlo en el visor se echa de menos, es un cambio chico: quitar el `isHeld` de `ImpactDetector.CanBeInvolved` y ponerle su propio umbral de velocidad.
- **Reciclar un enemigo que alguien tiene agarrado le deja algo de velocidad de lanzamiento.** `Respawn()` pone la velocidad a cero, pero el toolkit escribe la del lanzamiento **al final del frame**, después. En el juego no se nota porque la locomoción del enemigo sobrescribe x/z en el siguiente paso de física. **La Fase 3 lo tiene que tener presente** cuando el pool empiece a reciclar enemigos vivos.
- **`Staggered` sigue sin existir**, igual que al cerrar la 2a. Se promueve en la Fase 3.
- **La vibración real sigue sin probarse en automático**: en batch mode no hay dispositivo XR.

---

## FASE 3 — Horda: IA, oleadas y pooling

**Estado:** ⬜ Pendiente

**`EnemyLocomotion` ya no está aquí: se movió a la Fase 2a**, porque sin acercarse no hay nada que colgarse. Esta fase es escala, no comportamiento nuevo.

- `HordeSpawner` con oleadas crecientes, reusando `Pooler` del template en vez de Instantiate/Destroy. `HordeEnemy.Respawn()` es el paso de reciclado. **Ojo con reciclar un enemigo que un jugador tiene agarrado:** `Respawn()` ya cierra el agarre 0.25 s (`isGrabbable`) para que la mano no lo arrastre de vuelta, pero el toolkit escribe la velocidad del lanzamiento al final del frame, después de que `Respawn()` la puso a cero. Hoy no se nota porque la locomoción la sobrescribe; con un pool conviene comprobarlo.
- Máquina de estados completa: `Walking / Leaping / Latched / Staggered / Grabbed / Dead`. `Grabbed` ya existe desde la Fase 2b. **`Staggered` entra aquí**: un golpe interrumpe el salto de un enemigo que se te venía encima, y con 20-30 encima esa interrupción es la diferencia entre poder defenderte y no.
- La separación entre enemigos empieza a importar de verdad: con 3 dummies casi no se nota, con 30 es lo que evita que se apilen en un solo bulto.
- Contador de enemigos eliminados y oleada actual.

**Salida verificable:**
1. Play: aparecen oleadas y los dummies te buscan y se te cuelgan desde todos lados.
2. Puedes repelerlos a puñetazos y arrancándolos/aventándolos, y un golpe a tiempo **interrumpe** al que iba a saltarte.
3. Con 20-30 enemigos vivos simultáneamente sigue siendo jugable en Link, y no se apilan unos dentro de otros.
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
- **Replicar el colgado.** En la Fase 2a el enemigo aferrado se emparenta al ancla del jugador, y eso **no se replica solo**: reparentar un `NetworkObject` exige `TrySetParent` (con el ancla siendo también un `NetworkObject`) o, más barato, replicar solo *qué jugador y qué ancla* lo tienen y que cada cliente lo posicione localmente. Hay que decidirlo aquí, porque de eso depende que B vea al gnomo trepado en el hombro de A y no flotando en el aire.

**Salida verificable adicional:** si un enemigo se cuelga de A, B lo ve trepado en A, en el mismo punto del cuerpo; y cuando A se lo quita a golpes, B lo ve salir despedido.

**Salida verificable:**
1. Con Multiplayer Play Mode levantas 2 instancias (una en el visor por Link, otra en pantalla).
2. Ambas ven los mismos enemigos en las mismas posiciones.
3. Si el jugador A golpea un enemigo, el jugador B lo ve salir volando.
4. Si A agarra un enemigo, B lo ve agarrado y B **no** puede agarrarlo.
5. A avienta un enemigo contra otro y B ve ambos impactos.

---

## FASE 5 — Co-op completo y marcador compartido

**Estado:** ⬜ Pendiente

**Aquí es donde estar colgado por fin cuesta.** Hasta la Fase 4 los enemigos se te aferran sin consecuencia; esta fase cierra el bucle.

- **Vida del jugador y drenaje por enemigos colgados:** cada aferrado te chupa vida por segundo, así que el ritmo de pérdida es proporcional a cuántos te tengan encima. Esa es la condición de derrota — te sepultaron, no te noquearon.
- El drenaje reusa el enganche de feedback que la Fase 2a ya dejó puesto (haptics + destello rojo), ahora sí atado a un número.
- Condición de derrota compartida entre los dos jugadores.
- Marcador de oleada y eliminados sincronizado, visible para ambos. HUD en el visor, incluida la vida y cuántos te tienen agarrado.
- Manejo de entrada/salida de jugadores a media partida.

**Salida verificable:**
1. Con un enemigo colgado pierdes vida despacio; con tres, tres veces más rápido. Quitártelos detiene el drenaje.
2. Dos jugadores sobreviven oleadas juntos y ven el mismo número de oleada y el mismo total de eliminados.
3. Si uno se desconecta a media partida, la partida sigue y los enemigos que tenía agarrados **o colgados encima** no se quedan huérfanos.
4. Al perder, ambos ven la misma pantalla de resultado con cuánto aguantaron.

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
| 1 | ✅ | 2026-07-25 | Puñetazo local completo: `VelocityWindow`, `PunchResolver`, `PunchSettings`, `HandVelocityTracker`, `PunchDetector`, `HordeEnemy`. 99/99 verde (80 EditMode + 19 PlayMode), cobertura 86.2%, 8 mutaciones detectadas. Escena regenerada. Verificada en el visor. |
| 2a | ✅ | 2026-07-25 | Locomoción, salto, anclas y enganche completos. 218/218 verde (162 EditMode + 56 PlayMode), cobertura 89.1 %, 19 mutaciones detectadas. Corregido tras probar en el visor: spawn a 7-8.5 m en abanico por delante, avance a 1.6 m/s con techo de 3 m/s, primera llegada ~3.7 s, y knockback de 5-10 m (antes 30 cm). Escena regenerada. Verificada en el visor. |
| 2b | 🟡 | 2026-07-26 | Grip completo: arrancar de encima, cargar y aventar, más daño por impacto contra enemigos y contra el suelo. `ImpactKind`, `ImpactSettings`, `ImpactResolver`, `ImpactDetector`, `EnemyGrabInteractable`. Corregido tras la primera prueba en el visor: **mano abierta pega, mano cerrada agarra** — el puño se calla mientras el grip está apretado, y las manos pasan a `InputTriggerType.State` para que apretar antes de llegar sirva de algo. 263/263 verde (186 EditMode + 77 PlayMode), cobertura 90.4 %, 16 mutaciones detectadas. Escena regenerada. Después se subió el POC de 3 a **10 enemigos**, lo que obligó a ensanchar la banda de spawn a 7-9.5 m (186/186 EditMode en verde). **Falta volver a probarla en el visor.** |
| 3 | ⬜ | | |
| 4 | ⬜ | | |
| 5 | ⬜ | | |
| 6 | ⬜ | | |
