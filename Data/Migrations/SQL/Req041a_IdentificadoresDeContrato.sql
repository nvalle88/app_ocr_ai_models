/* =============================================================================
   REQ-041a - Codigo, numero y el triple: cual va en cada herramienta
   -----------------------------------------------------------------------------
   Nestor: "Tienes que tener cuidado con lo que envias, en algunos es codigo de
   contrato y en otros es numerocontrato, region, producto; ambos son id de la
   tabla de contrato, se utilizan en unos lados uno y en otros otro."

   Ya nos costo un fallo: consultar_ticket_sobre devolvia CERO para sobres que si
   tenian ticket, porque el chat mandaba 4102902 -el NUMERO- y el broker guarda
   1350017 -el CODIGO-. Y no fallaba con error: devolvia vacio, que es peor,
   porque parece "no hay nada" y es "preguntaste con la llave equivocada".

   -- Como esta repartido hoy, auditado sobre las 22 herramientas -----------

       piden el CODIGO     consultar_mis_reembolsos, consultar_detalle_sobre,
                           consultar_liquidacion_sobre, consultar_documentos_sobre
       piden el NUMERO     consultar_coberturas_plan, consultar_deducible_contrato,
                           historial_reembolsos_cliente_bd
       aceptan cualquiera  consultar_autorizaciones, consultar_ticket_sobre

   El patron no es capricho: las de SOBRES vienen del servicio de liquidaciones,
   que trabaja con el codigo; las de COBERTURA Y DEDUCIBLE vienen del servicio de
   contratos, que trabaja con el triple region + producto + numero.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @ancla nvarchar(100) = N'## Una sola pasada';
DECLARE @seccion nvarchar(max) = N'## El contrato tiene DOS identificadores. No los mezcles

Un contrato se identifica de dos maneras, y las herramientas no usan la misma:

| | Ejemplo | Quien lo usa |
|---|---|---|
| **Numero** (+ region + producto) | `Costa / IND / 4102902` | lo que ve el afiliado en su carnet |
| **Codigo** | `1350017` | el identificador interno |

Los dos vienen en el contrato ya resuelto, como `Numero` y `Codigo`. **Son
numeros parecidos que significan cosas distintas**, y equivocarse no da error:
da CERO filas, que parece "no tiene nada" y es "preguntaste con la llave que no
era". Ya paso: se dijo que un afiliado no tenia tickets cuando tenia cuatro.

**Cual manda cada una:**

- **Sobres y reembolsos** -`consultar_mis_reembolsos`, `consultar_detalle_sobre`,
  `consultar_liquidacion_sobre`, `consultar_documentos_sobre`- van con el
  **CODIGO** (`codigoContrato`).
- **Cobertura y deducible** -`consultar_coberturas_plan`,
  `consultar_deducible_contrato`- van con el **NUMERO**, y **siempre con region
  y codigoProducto**: sin el triple completo, el numero solo no identifica nada,
  porque se repite entre regiones y productos.
- **Autorizaciones y ticket** aceptan cualquiera de los dos. En esas manda
  ademas la **persona**, que es lo que de verdad acota al afiliado.

**Si una herramienta te devuelve vacio y esperabas datos, sospecha de esto
ANTES de decirle al afiliado que no tiene nada.** Reintenta con el otro
identificador. Y si sigue vacio, entonces si: dilo, pero como "no encontre",
nunca como "usted no tiene".

Y no le traslades esto al afiliado. El no sabe ni tiene por que saber que su
contrato tiene dos numeros: para el es "su contrato".

';

UPDATE dbo.Agent
   SET SystemPrompt = REPLACE(CONVERT(nvarchar(max), SystemPrompt), @ancla, @seccion + @ancla)
 WHERE Code = N'AGENTE_CHAT_CLIENTE'
   AND CONVERT(nvarchar(max), SystemPrompt) LIKE N'%' + @ancla + N'%'
   AND CONVERT(nvarchar(max), SystemPrompt) NOT LIKE N'%DOS identificadores%';

/* Y en la ficha de cada parametro, que es lo que el modelo lee al elegir. */
UPDATE dbo.OPAITool
   SET InputSchema = JSON_MODIFY(CONVERT(nvarchar(max), InputSchema),
        '$.properties.codigoContrato.description',
        N'CODIGO de contrato (el interno, p.ej. 1350017), NO el numero que ve el afiliado (4102902). El contrato resuelto trae los dos: aqui va el campo Codigo. Con el numero esto devuelve cero filas sin dar error.')
 WHERE Code IN (N'consultar_mis_reembolsos', N'consultar_detalle_sobre',
                N'consultar_liquidacion_sobre', N'consultar_documentos_sobre')
   AND ISJSON(CONVERT(nvarchar(max), InputSchema)) = 1;

SELECT Code,
       Advierte = CASE WHEN CONVERT(nvarchar(max), InputSchema) LIKE N'%NO el numero que ve el afiliado%'
                       THEN 'SI' ELSE 'no' END
  FROM dbo.OPAITool
 WHERE Code IN (N'consultar_mis_reembolsos', N'consultar_detalle_sobre',
                N'consultar_liquidacion_sobre', N'consultar_documentos_sobre')
 ORDER BY Code;
