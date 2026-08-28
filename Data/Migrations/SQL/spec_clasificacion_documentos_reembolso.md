# Especificación de referencia — Clasificación y tipificación de documentos de reembolso

> Entregada por el responsable (Néstor Valle) como GUÍA para el clasificador de Nexus.
> Se usa como especificación funcional: taxonomía de tipos, reglas de validación de
> factura, extracción de diagnósticos CIE10, valores y decisión de tipo predominante.
> Ampliación pedida sobre esta base: **un sobre puede contener VARIOS tipos de
> reembolso** (medicina + hospitalización + procedimientos…), por lo que hay que
> analizar **ítem por ítem** las facturas y **etiquetar (tags) cada documento y cada
> página**.

## 1. Documentos y validaciones

### 1.1 Factura médica válida (solo estas suman valores)
Un documento es FACTURA MÉDICA VÁLIDA únicamente si cumple TODO:
- contiene "FACTURA"
- contiene número de factura (ej. `No. 001-028-000000509`, `001-028-000000509`, `No 000003455`)
- contiene "NÚMERO DE AUTORIZACIÓN" o "CLAVE DE ACCESO"

Si falta cualquiera ⇒ NO es factura válida.

### 1.2 NO es factura (no suma valores)
comprobante de venta · liquidación de gastos · informe detallado ·
DETALLE DE LIQUIDACIÓN · LIQUIDACIÓN AMBULATORIA · DETALLE COMPROBANTES DE REEMBOLSO
⇒ `ValorTotal = 0.0`, pero SÍ se extraen diagnósticos CIE10 si existen.

### 1.3 Evitar duplicados
Si existe un desglose que lista facturas ya incluidas (p. ej. "DETALLE COMPROBANTES DE
REEMBOLSO"): NO sumar esos valores como facturas nuevas, NO duplicar `ValorTotal`.

## 2. Diagnósticos (CIE10)
- Extraer TODOS los CIE10 del texto: `{ "Codigo": "<CIE10>", "Valor": "<DESCRIPCION>" }`
- Normalizar quitando puntos: `M51.9` ⇒ `M519`
- Sin diagnósticos ⇒ `[]`

## 3. Valores (ValorTotal)
1. Solo suman las facturas válidas.
2. Una factura puede tener varias hojas; el TOTAL suele estar en la última.
   Priorizar el último valor de "TOTAL", "VALOR TOTAL", "TOTAL A PAGAR".
3. Varias facturas válidas en un archivo ⇒ sumar sus totales.
4. Sin facturas válidas ⇒ `0.0`. **No inventar valores.**

## 4. Taxonomía

### 4.1 TipoArchivo (exactamente UNO)
`LAB_IMA-FACTURA` · `BEN_ADI-FACTURA` · `GENERAL-SOPORTE` · `CON_MED-FACTURA` ·
`MED-FACTURA` · `LAB_CLI-FACTURA` · `ATE_HOS-FACTURA` · `GENERAL-FACTURA` ·
`PRO-FACTURA` · `TER-FACTURA`

REGLA CRÍTICA: con ≥1 factura válida ⇒ `*-FACTURA` (nunca `GENERAL-SOPORTE`).
Sin facturas válidas ⇒ `GENERAL-SOPORTE`.

### 4.2 ListaTipoArchivo (varios, sin duplicados)
Incluye TODOS los tipos detectados; aquí sí se permiten los `-SOPORTE`:
`LAB_CLI-SOPORTE` · `LAB_IMA-SOPORTE` · `CON_MED-SOPORTE` · `MED-SOPORTE` ·
`ATE_HOS-SOPORTE` · `PRO-SOPORTE` · `TER-SOPORTE` · `GENERAL-SOPORTE`
Los `-SOPORTE` solo van en `ListaTipoArchivo`, nunca en `TipoArchivo`.

## 5. Reglas de detección por palabras clave

### A) Si ES factura válida
- **BEN_ADI** beneficio/adicional: beneficio, vasectomía, ligaduras, adicional, cristales
  ópticos, lentes, leche medicada, fórmula infantil especial/hidrolizada/elemental/sin
  lactosa/hipoalergénica/de soya, leche de inicio especial, suplemento nutricional oral,
  alimentación enteral, nutrición enteral domiciliaria, nutramigen, alimentum, neocate,
  pregestimil, similac, enfamil, nan, aptamil, isomil, prosobee, elecare, peptamen,
  vivonex, ensure, pediasure, nutren, plantillas, zapatos ortopédicos, audífonos, muletas,
  silla de ruedas, alquiler de equipos, aparatos ortopédicos, medias elásticas, collarín,
  prótesis externa, enfermería domiciliaria, cuidador, terapia en casa, transporte
  sanitario, subsidio, reintegro, bono
- **LAB_CLI** laboratorio, análisis, hematología, bioquímica
- **LAB_IMA** rayos x, rx, ecografía, tomografía, resonancia, imagen
- **CON_MED** consulta, honorarios médicos, atención médica, consulta de especialidades,
  consulta especialista, consulta médica especialista, consulta por especialidad,
  honorarios consulta especialista
- **MED** medicamento, medicina, farmacia
- **ATE_HOS** hospitalización, habitación, internación, clínica, sala, emergencias
- **PRO** endoscopia, colonoscopia, gastroscopia, broncoscopia, cistoscopia, laparoscopia,
  sutura, retiro de puntos, curación, desbridamiento, drenaje de absceso, colocación de
  yeso, retiro de yeso, inmovilización, reducción de fractura, tracción, infiltración,
  punción, biopsia, aspiración, colocación de sonda, cateterismo, nebulización, vendaje
  funcional, monitoreo fetal, monitor fetal
  - Regla especial: prestador "Hospital Metropolitano" + "CONTROL ELECTRO. LAT 30'" o
    "CONTROL ELECTRO LAT 30" ⇒ interpretar como **Monitoreo fetal** ⇒ PRO
- **TER** fisioterapia, terapia, rehabilitación
- Obligatorio: determinar una de las categorías `*-FACTURA`.

### B) Si NO es factura válida
Misma lógica semántica con sufijo `-SOPORTE` (incluye además: radiografía, ultrasonido,
tac, mamografía, diagnóstico por imagen, consulta externa, medicamentos, medicinas,
receta, prescripción, emergencia, atención hospitalaria, procedimiento, terapia física /
respiratoria / ocupacional). Sin evidencia suficiente ⇒ `GENERAL-SOPORTE`.

## 6. Decisión del TipoArchivo predominante
Con facturas válidas:
1. Prioriza el **contexto global** (tipo de atención, establecimiento, encabezados,
   composición de rubros). Atención integral de clínica/hospital/emergencia/internación
   con múltiples rubros ⇒ `ATE_HOS-FACTURA`. Un solo tipo de servicio claro ⇒ ese `*-FACTURA`.
2. Si el contexto no alcanza ⇒ gana el `*-FACTURA` del rubro/factura con mayor `ValorTotal`.
3. Empate ⇒ prioridad:
   `ATE_HOS > PRO > CON_MED > LAB_CLI > LAB_IMA > MED > TER > GENERAL`
Sin facturas válidas ⇒ `GENERAL-SOPORTE`.

## 7. ResumenDiagnosticos (agrupado final)
Por archivo: si `ValorTotal > 0` y hay diagnósticos, asigna el `ValorTotal` COMPLETO a
cada diagnóstico de ese archivo (2 dx y total 100 ⇒ cada dx recibe 100).
`ValorTotal = 0` o sin diagnósticos ⇒ no aporta.
Luego agrupa por `Codigo` y suma: `{ "Codigo", "Valor", "ValorTotal" }`.

## 8. Formato base de respuesta (referencia)
```json
{
  "Ficheros": [
    { "UrlArchivo": "{fileName}.{ext}",
      "Resultado": { "TipoArchivo": "GENERAL-SOPORTE", "ListaTipoArchivo": "GENERAL-SOPORTE",
                     "ValorTotal": 0.0, "ListaDiagnostico": [] } }
  ],
  "ResumenDiagnosticos": [ { "Codigo": "A22", "Valor": "Descripción", "ValorTotal": 0.0 } ]
}
```

## 9. Ampliación requerida (alcance de este desarrollo)
1. **Tags por documento Y por página**: cada página del PDF debe quedar etiquetada con su
   tipo (`MED-FACTURA`, `ATE_HOS-SOPORTE`, …) y sus marcas (tiene factura válida, nº de
   factura, autorización, diagnósticos, totales).
2. **Ítem por ítem**: desglosar los rubros de cada factura con su tipo y valor, porque un
   mismo sobre/factura puede mezclar medicina, hospitalización, procedimientos, etc.
3. **Split del reembolso por tipo**: el sobre se divide en sub-reembolsos por tipo con su
   valor (p. ej. MED $120,50 · ATE_HOS $890,00 · PRO $75,00) + tipo predominante.
4. **Persistencia**: guardar el OCR **por página** (hoy se guarda texto plano y se pierde
   la estructura de `AnalyzeResult.Pages`), más las tablas de clasificación, ítems y tags.
