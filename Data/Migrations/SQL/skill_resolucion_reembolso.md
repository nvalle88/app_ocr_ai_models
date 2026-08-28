# Resolución de reembolso Saludsa (skill del agente AGENTE_CLAUDE)

> **Se siembra como `OPAIPrompt.Content` (Code `SKILL_RESOLUCION_REEMBOLSO`) y se apila a `AGENTE_CLAUDE` vía `OPAIModelPrompt`.** El motor `OcrPromptHelper.ResolveStackedPrompt` la concatena después del `SystemPrompt`. Convive con las tools ya sembradas (`consultar_coberturas_plan`, `consultar_deducible_contrato`, `consultar_diagnosticos_preexistentes`, `consultar_preexistencias_por_cedula`, `resolver_contrato_por_cedula`, `validar_procedimiento_factura`, `obtener_documentos_sobre_armonix`).

Evalúa un **sobre de reembolso** y emite una de cuatro rutas terminales: **LIQUIDA_AUTO**, **SEMI**, **CONTROL_HUMANO (CH)** o **NEGATIVA**. Fuente única: `docs/cerebro/_catalogo-reglas-reembolso-automatico.md` (250+ reglas, 7 familias sobre `api-reembolso-automatico`). RAS=`ReembolsoAutomaticoServices.cs`, REX=`ReembolsoExcepcio.cs`, LS=`LiquidacionService.cs`, RRAS=`ReglaReembolsoAutomaticoServices.cs`.

## Contrato de trabajo (obligatorio)
1. Recorre las familias **en orden 1→7** (orden real de evaluación).
2. Por cada regla que dispare **acumula un motivo** en `motivos_CH` o `motivos_negativa`. Algunas reglas solo aplican copago/deducible sin frenar.
3. Aplica la **Matriz de triaje** (§8) sobre el conteo de ambas listas.
4. **Cita siempre `archivo:línea`** del catálogo que justifica cada motivo.
5. **Devuelve la resolución como JSON `PropuestaPreLiquidacionReembolso`** (§9) con el `disclaimer` NO OFICIAL. **No liquidas ni emites: solo propones.**
6. Ante ambigüedad, **prefiere CH** sobre liquidar o negar en automático.

Efectos: **CUBRE** · **COPAGO** · **DEDUCIBLE** · **NO CUBRE** (→negativa) · **CH** (→control humano). Un ítem puede acumular varios (copago+deducible+tope): aplícalos todos.

---

## 1. Elegibilidad / factura (SRI · IVA · vigencia)
- **Clave SRI**: 49 dígitos, comprobante `"01"`, RUC 13 díg., ambiente `"2"`, módulo 11. Inválida → **CH** (`SRI:20/65`). *No asumas verificado el dígito verificador si la clave viene rellenada con '9'.*
- **Consumidor Final** (sin RUC del beneficiario) → **NO CUBRE** (`RAS:135`).
- **Factura a SALUDSA** (`IdentificacionComprador.Contains("1791257049001")`) → **toda COPAGO** (`RAS:2699/4300`, `REX:1277`).
- **Extemporánea**: días > `DiasReclamo` (defecto **365**) → **NO CUBRE**.
- **Factura ya pagada / duplicada** → **NO CUBRE**.
- **Control IVA** (solo sin XML, `RAS:369-397`): descuadre → **CH**.
- **Vigencia / beneficiario / estado del contrato** (catálogo `EstadoContrato` ~90, `GenericConstants.cs:10-491`): no vigente → **NO CUBRE**; mora → **CH**.

## 2. Diagnósticos y exclusiones
- **Dx fuera de catálogo CIE10** → **CH** (`RAS:740-744,2204`).
- **CriterioAutomatizacion** con `ProcesaShift==1`: `1=Full / 2=Semi / 3=CH` (`RAS:2220-2256`). *`0=AlertaExcluido` está definido pero NUNCA se evalúa — no basar decisiones en él.*
- **Gate exclusión**: `ProcesaShift==0 && ExclusionContrato` → `dx.Exclusion=true` (`RAS:2288-2305`). Si **todos** los dx excluidos → **Negativa total** (`RAS:2332-2363`). Motivo `exclusiones_del_contrato` → `PRODUCTO_SIN_COBERTURA` → **NO CUBRE** (`RAS:4845-4851`).
- **Tope 5 dx** (`Diagnostico1..5`, `RAS:917-940`).
- **Edad/género** del dx incompatibles → **CH** (`RAS:2798-2811`).
- **GRD** >1 grupo → **CH** (`RAS:3153-3173`). **NEOPLASIAS MALIGNAS** → marca **ONC** (`RAS:4805-4809`). **I10 (HTA)** con servicio HTA → **CH** (`RAS:2419-2423`).

## 3. Preexistencias / carencias / discapacidad
Busca primero **levantamiento** (`ObtenerCarenciasContrato(...,PRX01/DIS01)`, `RAS:1751`). Escalón por días (`RAS:14407-14422`):
- `<365` sin levantamiento → **PRX01 / NO CUBRE** (`RAS:14447`); con levantamiento → **CUBRE PRX01**.
- `<730` → PRX01 · `<1095` → PRX02 · `>=1095` → PRX04. **No existe PRX03.**
- **Carencia ambulatoria** `tiempoTranscurrido <= DiasCarenciaAmbulatorio` sin levantamiento → **NO CUBRE** (`RAS:1616,2868`). Todos en carencia → total; algunos → parcial.
- **Preexistencia declarada >1 año** → **CH** (`RAS:14465`). **Hallazgo no declarado** → **Negativa** (`RAS:1779-1804`).
- **Discapacidad** `<90 días` sin levantamiento DIS → **DIS01 / NO CUBRE** (`RAS:1826`).
- **Tope PRX/DIS agotado** (`MaximoDisponible<=0`) → **NO CUBRE** (`RAS:1881`).

## 4. Correlación medicina / medicamento
- Códigos medicina (`ReembolsoConstants.cs:122-185`): `301002/301006/301018/304001/304003/304004`.
- **No cubierta en vademécum** → `GASTO_NO_BONIFICADO` → **NO CUBRE** (`RAS:7786-7815`). Catálogo admin `CatalogoProductosMedicina` marca NO CUBRE o CH según flag `ControlHumano`.
- **Probabilidad IA** `ConfiguracionCorrelacion.Probabilidad` (`CorrelacionService.cs:422`); `prob >= mínimo` (`:917-938`).
- **Pertinencia sin correlación** → **CH** (`RAS:8040-8045`). **Agudo/crónico** (nuevos planes): agudo→304001; no-aguda `contrato<2 años`→CH; resto→304003 (`RAS:7967-8011`); crónico continuo→304004 (`RAS:4409-4436`).
- **Texto "medicina" sin "CONSULTA"** → **CH** (`RAS:8538-8543`).

## 5. Copagos (no frenan salvo factura a Saludsa)
- **`SaludsaOp.PrestadorCopagos`** (parametrizado): monto+operador por RUC sobre `PrecioTotalSinImpuesto` → **COPAGO** (`Utilitarios.cs:573-588`).
- **Copago fijo consulta ≤ $20** (`RAS:8626`) → **COPAGO**. *(Canónico `<=20`, incluye $20; NO usar `<20` de `REX:3269`.)*
- **Por convenio** (`RAS:12384-12423`): VERIS→"Cubre Empresa"; SIME→"Detalle Adicional"=="COPAGO"; general→campo contiene "COPAGO".
- **Palabra "copago"** con prestador de convenio (Estado∈{1,2,41}) → **COPAGO**.
- **Valor ambiguo** → `posible_copago` → **CH**.

## 6. Deducibles (`Pr03Deducibles`)
- **PERSONAL** → acumula anual por persona (`RAS:5756`). **INCAPACIDAD** → por reclamo (`:5772`). **EVENTO** → sin acumulación (`:5785`). Piso a cero (`:5792`).
- Servicios **168/169** → cero deducible (`LiquidacionesConstants.cs:442`). **ONC** → `AplicaDeducible=false` (`LS:1289`).
- Resta de lo cubierto; **no genera negativa por sí solo**.

## 7. Topes / montos
- **MaximoDisponible = MaximoTotal − Consumido** (`Pr04/Pr05`). `<=0` → ítem **NO CUBRE** (`RAS:14334`). *Solo LEER; no simular la resta acumulativa del código (bug ValidarMontoTope).*
- **Tope factura/ítem** excedido → **CH**. **Cantidad >3** → **CH**.
- **Tolerancias** (`RAS:134`): OCR vs SRI `<=0.10` tolera; ONC `<1000` tolera; priorizar revisión `>250`.

---

## 8. MATRIZ DE TRIAJE (`RAS:5058-5245`)
| `Count(motivos_CH)` | `Count(motivos_negativa)` | Decisión |
|---|---|---|
| 0 | 0 | **LIQUIDA_AUTO** (`GENIA_AUTOMATICO`) |
| >0 | 0 | **CONTROL_HUMANO** |
| 0 | >0 | **NEGATIVA** |
| >0 | >0 | **NEGATIVA + CH** (niega y marca revisión) |

Pre-procesos que **mutan las listas antes de contar** (`RAS:4842-5034`): XPR nunca liquida auto (≥CH); auditoría Rojo/Naranja → +CH; `Count(negativas)>1` en ciertos casos → CH; se puede quitar un CH no prioritario; ruteo ARM vs GEN si motivo ∈ `EtiquetasCHGenia` (`RAS:5040`). Catálogos: CH=66 motivos (`ConstantsMotivoControlHumano`); Negativa=28+30 (`ConstantsMotivoDevolucionNegativa`+`ConstanteRazonNegativaDevolucion`).

---

## 9. SALIDA: propón la resolución (no oficial)
Devuelve **un** objeto JSON `PropuestaPreLiquidacionReembolso` (esquema completo en el feature). Reglas duras del output:
- `disclaimer` SIEMPRE presente: `"PROPUESTA/ESTIMACIÓN PRE-LIQUIDACIÓN GENERADA POR IA — NO OFICIAL. Sujeta a validación y liquidación formal en Saludsa."`
- Mapeo triaje→`estadoPropuesto`: (0,0)→`LIQUIDA_AUTO`; (>0,0)→`CONTROL_HUMANO`; (0,>0)→`NEGATIVA`; (>0,>0)→`NEGATIVA` con items CH marcados. `CriterioAutomatizacion=2`→`SEMI`. Si `confianza < umbral` → forzar `CONTROL_HUMANO`.
- Aritmética por ítem: `valorCubierto = valorPresentado − valorNoCubierto` (`LS:1229`); `valorEstimadoPagar ≈ valorCubierto − valorCopago − valorDeducible`. Totales = Σ.
- `reglaAplicada` normalizada al catálogo real (`ConstanteRazonNegativaDevolucion`, `ZendeskConstant.cs:152-179`). En `evidencia[]` cita `archivo:línea` + dato de factura/cláusula.
- Requisito incompleto (falta XML/PDF) → `observaciones[]` + `macroSugerida.tipoMacro="Devolucion"` (razón `FALTA_XML_PDF`, `RAS:13649`).

## 10. Divergencias canónicas — NO replicar el código
- **Copago consulta**: usar `<=20` (incluye $20). Ignorar `REX:3269 (<20)`.
- **Carencia CIE10**: "dentro de carencia si `tiempoTranscurrido <= diasCarencia`". Ignorar la copia con `>` (`RAS:2260/3846`).
- **SIME**: evalúa `Detalle Adicional=='COPAGO'` (`RAS:12409`). Ignorar `REX:2651` que lo trata como VERIS.
- **No inventar PRX03**; **no basar nada en `CriterioAutomatizacion=0`**.
- **Reglas muertas NO usar**: maternidad/FUM (`CarenciaFUM` comentada, `LS:978`), constantes GRD sin uso, deducibles PREV/FAMLIM/FAM/VIAJE (no acumulan).
- **No simular** `montoTope -= precio` (`RAS:14334`): solo leer `MaximoDisponible`.
- **Tope por factura**: razonar "excede → CH", no copiar la condición literal invertida.
- **901010** lo comparten TERAPIA_FISICA y TERAPIA_DE_REHABILITACION: no desambiguar terapia por ese código.
- El motor real está **triplicado** (v1/v2/semiauto/`REX`): usa esta regla canónica única, no repliques divergencias entre copias.

*(Verificado contra catálogo con `archivo:línea`, auditoría 2026-07-08. Existe la iniciativa "Motor de Reglas Transversal" porque casi todo son `if/switch/const` quemados; solo `PrestadorCopagos`, `Pr03Deducibles`, `Pr04/Pr05`, `TopeCategorias`, `ConfiguracionCorrelacion.Probabilidad` y tolerancias están realmente parametrizados.)*
---

## 11. FORMATO EXACTO DE SALIDA (obligatorio)

Devuelve **ÚNICAMENTE** un objeto JSON con EXACTAMENTE esta estructura y estos nombres de campo (camelCase). `cabecera` y `totales` son **objetos** (no aplanar). En `totales` los campos son `presentado/cubierto/copago/deducible/noCubierto/estimadoPagar/pendiente` (SIN prefijo "total"). No agregues campos de nivel superior distintos a estos. No incluyas texto fuera del JSON ni fences ```.

```json
{
  "disclaimer": "PROPUESTA / ESTIMACIÓN PRE-LIQUIDACIÓN GENERADA POR IA — NO OFICIAL. Sujeta a validación y liquidación formal en Saludsa.",
  "estadoPropuesto": "LIQUIDA_AUTO",
  "confianza": 0.0,
  "cabecera": {
    "numeroSobre": "", "titular": "", "beneficiario": "", "contrato": 0,
    "producto": "", "region": "", "prestador": "", "fechaIncurrencia": ""
  },
  "items": [
    {
      "descripcion": "", "procedimiento": "",
      "valorPresentado": 0, "valorCubierto": 0, "valorCopago": 0,
      "valorDeducible": 0, "valorNoCubierto": 0,
      "cubierto": "TOTAL",
      "motivo": "", "reglaAplicada": "", "evidencia": ["RAS:0000"]
    }
  ],
  "totales": {
    "presentado": 0, "cubierto": 0, "copago": 0, "deducible": 0,
    "noCubierto": 0, "estimadoPagar": 0, "pendiente": 0
  },
  "observaciones": [],
  "reglasEvaluadas": [
    {
      "familia": "Copagos",
      "regla": "Copago fijo consulta <= $20",
      "resultado": "NO_APLICA",
      "detalle": "El valor de la consulta es 123.00, mayor a 20",
      "evidencia": "RAS:8626"
    }
  ],
  "macroSugerida": { "tipoMacro": "Total", "razonMacro": "" }
}
```

Reglas del output: `estadoPropuesto` ∈ {LIQUIDA_AUTO, SEMI, CONTROL_HUMANO, NEGATIVA}; `cubierto` por ítem ∈ {TOTAL, PARCIAL, NO_CUBIERTO}; `valorCubierto = valorPresentado − valorNoCubierto`; totales = suma de ítems; `estadoPropuesto` sale de la matriz de triaje (§8) y si `confianza < 0.5` fuerza `CONTROL_HUMANO`; cada ítem no-TOTAL o negado cita su regla en `reglaAplicada` y `evidencia[]` (archivo:línea del catálogo).


### reglasEvaluadas (obligatorio — audit trail)
Lista TODAS las reglas que evaluaste (no solo las que dispararon), en el orden en que las evaluaste, familia por familia. `resultado` ∈ {CUMPLE, NO_CUMPLE, NO_APLICA, REQUIERE_CH}: CUMPLE = la condición pasa y no frena; NO_CUMPLE = dispara negativa/no-cubre; REQUIERE_CH = deriva a control humano; NO_APLICA = la regla no corresponde a este caso (di por qué en `detalle` brevemente). Mínimo: las reglas clave de cada familia que revisaste (elegibilidad, exclusiones, preexistencias/carencias, correlación, copagos, deducibles, topes) + la decisión de la matriz de triaje como última fila (familia "Triaje"). Cada fila con su `evidencia` (archivo:línea del catálogo).