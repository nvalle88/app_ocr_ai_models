/* =============================================================================
   REQ-034b - El COLLATE tambien del lado del parametro
   -----------------------------------------------------------------------------
   REQ-034a puso COLLATE ..._CI_AI en la COLUMNA para que buscar_medicina dejara
   de ser sensible a tildes. Se aplico, se desplegó... y "acido folico" con
   tildes seguia devolviendo 0 filas. Comprobado y no supuesto: se desplego
   primero y se volvio a medir.

   La causa: el ejecutor manda el parametro como NVARCHAR cuando el texto NO es
   ASCII puro -y "acido folico" con tildes no lo es-. Al comparar un varchar con
   un nvarchar, SQL Server convierte el varchar a nvarchar y en esa conversion
   se PIERDE el COLLATE explicito de la columna. El arreglo se anulaba a si
   mismo justo en el unico caso para el que existia.

   Comprobado en la base, contra CreditoFarmacia.CFMedicina:

       Descripcion LIKE '%acido folico%' con tildes ................... 0 filas
       Descripcion COLLATE ..._CI_AI LIKE '%acido folico%' con tildes .. 4 filas

   Asi que la intercalacion va en los DOS lados: la columna ya la trae de 034a,
   y aqui el parametro se baja a varchar y se marca igual. La comparacion entera
   queda en _CI_AI y las tildes dejan de importar.
   ============================================================================= */

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    DECLARE @actual sysname = DB_NAME();
    RAISERROR('Esta migracion es solo para db-nexus-test. Base actual: %s', 16, 1, @actual);
    RETURN;
END

SET NOCOUNT ON;

DECLARE @v1 nvarchar(400) = N'LIKE ''%'' + @nombre + ''%''';
DECLARE @n1 nvarchar(400) = N'LIKE (''%'' + CAST(@nombre AS varchar(400)) + ''%'') COLLATE SQL_Latin1_General_CP1_CI_AI';
UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v1, @n1)
 WHERE Code = N'buscar_medicina'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @v1 + N'%';

DECLARE @v2 nvarchar(400) = N'LIKE ''%'' + @principioActivo + ''%''';
DECLARE @n2 nvarchar(400) = N'LIKE (''%'' + CAST(@principioActivo AS varchar(400)) + ''%'') COLLATE SQL_Latin1_General_CP1_CI_AI';
UPDATE dbo.OPAITool
   SET BindingConfig = REPLACE(CONVERT(nvarchar(max), BindingConfig), @v2, @n2)
 WHERE Code = N'buscar_medicina'
   AND CONVERT(nvarchar(max), BindingConfig) LIKE N'%' + @v2 + N'%';

SELECT Code, Json = ISJSON(CONVERT(nvarchar(max), BindingConfig))
  FROM dbo.OPAITool WHERE Code = N'buscar_medicina';
