/* =============================================================================
   REQ-019m — Tipificacion profunda de los documentos del sobre
   -----------------------------------------------------------------------------
   QUE AGREGA
     1. DocumentoClasificacion: quien EMITE la factura (RUC, razon social, tipo de
        establecimiento, ciudad, pais), sus datos fiscales (autorizacion, fecha,
        subtotal, IVA, moneda), el tipo CLINICO del soporte, el medico tratante,
        la fecha de atencion y los datos ANONIMOS del paciente (edad/sexo).
        >>> Deliberadamente NO hay columna para el nombre del paciente: el
            requisito es no escribirlo en ningun sitio.
     2. DocumentoProcedimiento: los CPT detectados por documento/pagina, con el
        codigo de liquidacion Saludsa que les corresponde.
     3. ClasificacionSobre: a nivel de SOBRE, el tipo de atencion
        (HOSPITALARIO / HOSPITAL_DIA / AMBULATORIO / DESCONOCIDO), los 13
        indicadores que lo sustentan y la justificacion.
     4. CatalogoCodigoLiquidacion: el catalogo de codigos de liquidacion de
        Saludsa, ADMINISTRABLE EN TABLA. El cruce CPT -> codigo de liquidacion se
        hace por JOIN en el controlador, NO en el prompt: un lookup no se le pide
        a un modelo, y asi el catalogo se edita sin re-desplegar ni re-entrenar.

   Idempotente y con guarda de base.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF DB_NAME() <> 'db-nexus-test'
BEGIN
    RAISERROR('Este script solo debe correr en db-nexus-test. Base actual: %s', 16, 1, @@SERVERNAME);
    RETURN;
END
GO

/* ---------------------------------------------------------------------------
   1) Columnas nuevas en DocumentoClasificacion
   --------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.DocumentoClasificacion', 'TipoSoporte') IS NULL
BEGIN
    ALTER TABLE dbo.DocumentoClasificacion ADD
        -- Tipo clinico del documento (taxonomia E4 del prompt del clasificador)
        TipoSoporte              varchar(40)   NULL,
        ResumenSoporte           varchar(600)  NULL,
        -- Emisor = el PRESTADOR que factura (no el paciente)
        EmisorRuc                varchar(20)   NULL,
        EmisorNombre             varchar(250)  NULL,
        EmisorNombreComercial    varchar(250)  NULL,
        EmisorTipo               varchar(30)   NULL,
        EmisorCiudad             varchar(100)  NULL,
        EmisorPais               varchar(60)   NULL,
        -- Datos fiscales de la factura
        NumeroAutorizacion       varchar(60)   NULL,
        FechaEmision             date          NULL,
        Subtotal                 decimal(18,2) NULL,
        Iva                      decimal(18,2) NULL,
        Moneda                   varchar(10)   NULL,
        -- Paciente ANONIMO: edad/sexo si constan. NUNCA el nombre.
        PacienteEdad             int           NULL,
        PacienteSexo             varchar(10)   NULL,
        PacienteEsTitular        bit           NULL,
        -- Medico tratante: aqui SI va el nombre (es prestador)
        MedicoNombre             varchar(250)  NULL,
        MedicoEspecialidad       varchar(150)  NULL,
        MedicoRegistro           varchar(60)   NULL,
        FechaAtencion            date          NULL;
END
GO

SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DocumentoClasificacion_EmisorRuc')
    CREATE INDEX IX_DocumentoClasificacion_EmisorRuc
        ON dbo.DocumentoClasificacion (EmisorRuc) WHERE EmisorRuc IS NOT NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DocumentoClasificacion_TipoSoporte')
    CREATE INDEX IX_DocumentoClasificacion_TipoSoporte
        ON dbo.DocumentoClasificacion (TipoSoporte) WHERE TipoSoporte IS NOT NULL;
GO

/* ---------------------------------------------------------------------------
   2) Procedimientos (CPT) detectados por documento
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.DocumentoProcedimiento', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DocumentoProcedimiento
    (
        Id                  bigint        IDENTITY(1,1) NOT NULL,
        DataFileId          int           NOT NULL,
        PageNumber          int           NULL,
        CodigoCpt           varchar(20)   NULL,          -- tal como lo trae el documento
        Descripcion         varchar(500)  NOT NULL,
        -- Resultado del cruce contra CatalogoCodigoLiquidacion (JOIN, no IA)
        CodigoLiquidacion   varchar(20)   NULL,
        RubroLiquidacion    varchar(250)  NULL,
        CreatedDate         datetime      NOT NULL CONSTRAINT DF_DocumentoProcedimiento_Created DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_DocumentoProcedimiento PRIMARY KEY (Id),
        CONSTRAINT FK_DocumentoProcedimiento_DataFile FOREIGN KEY (DataFileId)
            REFERENCES dbo.DataFile (Id) ON DELETE CASCADE
    );

END
GO

SET QUOTED_IDENTIFIER ON;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DocumentoProcedimiento_DataFile')
    CREATE INDEX IX_DocumentoProcedimiento_DataFile ON dbo.DocumentoProcedimiento (DataFileId);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_DocumentoProcedimiento_Cpt')
    CREATE INDEX IX_DocumentoProcedimiento_Cpt ON dbo.DocumentoProcedimiento (CodigoCpt) WHERE CodigoCpt IS NOT NULL;
GO

/* ---------------------------------------------------------------------------
   3) Clasificacion a nivel de SOBRE: tipo de atencion + indicadores
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.ClasificacionSobre', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ClasificacionSobre
    (
        Id                  bigint          IDENTITY(1,1) NOT NULL,
        CaseCode            uniqueidentifier NOT NULL,
        -- HOSPITALARIO | HOSPITAL_DIA | AMBULATORIO | DESCONOCIDO
        TipoAtencion        varchar(30)     NOT NULL,
        Justificacion       varchar(2000)   NULL,
        -- Los 13 indicadores como JSON [{nombre,presente,docId}]
        IndicadoresJson     nvarchar(max)   NULL,
        TipoPredominante    varchar(30)     NULL,
        TotalSobre          decimal(18,2)   NULL,
        ModelCode           varchar(50)     NULL,
        VersionNumber       int             NOT NULL CONSTRAINT DF_ClasificacionSobre_Version DEFAULT (1),
        IsCurrent           bit             NOT NULL CONSTRAINT DF_ClasificacionSobre_Current DEFAULT (1),
        CreatedDate         datetime        NOT NULL CONSTRAINT DF_ClasificacionSobre_Created DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_ClasificacionSobre PRIMARY KEY (Id),
        CONSTRAINT CK_ClasificacionSobre_Tipo CHECK
            (TipoAtencion IN ('HOSPITALARIO','HOSPITAL_DIA','AMBULATORIO','DESCONOCIDO'))
    );

END
GO

SET QUOTED_IDENTIFIER ON;
GO
-- Una sola clasificacion vigente por caso (el resto queda como historico)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_ClasificacionSobre_Vigente')
    CREATE UNIQUE INDEX UQ_ClasificacionSobre_Vigente
        ON dbo.ClasificacionSobre (CaseCode) WHERE IsCurrent = 1;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ClasificacionSobre_Case')
    CREATE INDEX IX_ClasificacionSobre_Case ON dbo.ClasificacionSobre (CaseCode);
GO

/* ---------------------------------------------------------------------------
   4) Catalogo de codigos de liquidacion Saludsa (administrable)
   --------------------------------------------------------------------------- */
IF OBJECT_ID('dbo.CatalogoCodigoLiquidacion', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CatalogoCodigoLiquidacion
    (
        Codigo      varchar(20)   NOT NULL,
        Rubro       varchar(250)  NOT NULL,
        Descripcion varchar(1000) NULL,
        IsActive    bit           NOT NULL CONSTRAINT DF_CatalogoCodigoLiq_Active DEFAULT (1),
        CreatedDate datetime      NOT NULL CONSTRAINT DF_CatalogoCodigoLiq_Created DEFAULT (GETUTCDATE()),
        CONSTRAINT PK_CatalogoCodigoLiquidacion PRIMARY KEY (Codigo)
    );
END
GO

/* Seed idempotente: solo inserta los que falten (MERGE por Codigo). */
;WITH src (Codigo, Rubro, Descripcion) AS (
    SELECT * FROM (VALUES
     ('11044','Limpieza Quirurgica','Procedimiento quirurgico para desbridamiento oseo profundo de heridas infectadas, traumatismos o ulceras con tejido necrotico.'),
     ('19120','Excision de lesion mamaria (quiste/fibroadenoma)','Reseccion quirurgica de lesiones benignas en la mama, como quistes o fibroadenomas.'),
     ('19240','Mastectomia Modificada Radical','Extirpacion total de la mama con diseccion axilar preservando los musculos pectorales.'),
     ('19400','Marcaje de Mama','Colocacion de guia o marcador previo a biopsia o cirugia mamaria.'),
     ('27130','Artroplastia, reemplazo total de cadera','Reemplazo quirurgico de la articulacion de la cadera por una protesis, con o sin injerto oseo.'),
     ('29806','Artroscopia Quirurgica del Hombro, Capsulorrafia','Reparacion artroscopica del hombro con sutura de la capsula articular (inestabilidad glenohumeral).'),
     ('29807','Reparacion Lesion Tipo SLAP','Reparacion artroscopica de lesion labral superior tipo SLAP en el hombro.'),
     ('29827','Artroscopia Hombro; Reparacion Manguito Rotador','Reparacion de desgarros del manguito rotador mediante tecnica artroscopica.'),
     ('29881','Artroscopia con Menisectomia','Reseccion parcial de menisco (medial o lateral) via artroscopica.'),
     ('29884','Lisis de Adherencias (Rodilla)','Liberacion de adherencias intraarticulares en la rodilla por artroscopia.'),
     ('29888','Reconstruccion Ligamento Cruzado Anterior (LCA)','Cirugia artroscopica para reconstruccion del LCA utilizando injertos.'),
     ('30300','Retiro Cuerpo Extrano Nariz','Extraccion de objeto extrano alojado en cavidad nasal.'),
     ('31500','Intubacion Endotraqueal','Insercion de tubo endotraqueal para garantizar via aerea y ventilacion asistida.'),
     ('31530','Laringoscopia con Extraccion de Cuerpo Extrano','Visualizacion directa de laringe con remocion de objeto extrano.'),
     ('32422','Tubo de Torax','Insercion de tubo pleural para drenaje de aire, liquido o sangre en cavidad toracica.'),
     ('33960','ECMO (primeras 24 horas)','Asistencia circulatoria y respiratoria extracorporea durante las primeras 24 horas.'),
     ('33961','ECMO (dias subsecuentes)','Continuacion de soporte vital por ECMO posterior a las primeras 24 horas.'),
     ('36215','Colocacion Cateter en Sistema Arterial','Insercion selectiva de cateter en arteria para estudios diagnosticos o terapeuticos.'),
     ('36262','Retiro de Implantofix','Extraccion de acceso vascular tipo implantofix.'),
     ('36520','Aferesis Plaquetaria','Procedimiento de recoleccion de plaquetas por aferesis.'),
     ('36533','Colocacion de Implantofix / Hemodialisis','Insercion de cateter permanente tipo implantofix para acceso vascular en hemodialisis.'),
     ('36822','ECMO Vias de Acceso (Canulacion)','Insercion de canulas venosas y/o arteriales para iniciar soporte con ECMO.'),
     ('38221','Biopsia de Medula con Aguja o Trocar','Obtencion de muestra de medula osea mediante puncion con aguja o trocar.'),
     ('43235','Endoscopia digestiva alta diagnostica','Visualizacion del tracto digestivo superior con fines diagnosticos.'),
     ('43239','Endoscopia mas Biopsia','Endoscopia digestiva alta con toma de muestras de tejido para estudio histopatologico.'),
     ('43247','Endoscopia para remocion de cuerpo extrano','Extraccion endoscopica de cuerpo extrano en esofago, estomago o duodeno.'),
     ('43248','Endoscopia, insercion alambre y dilatacion','Paso de guia e instrumentacion para dilatar estenosis del tracto digestivo superior.'),
     ('43249','Endoscopia, dilatacion por balon','Dilatacion de estenosis esofagica o pilorica mediante balon endoscopico.'),
     ('43250','Endoscopia, remocion polipos por cauterio','Polipectomia endoscopica usando tecnica de electrocauterio.'),
     ('43251','Endoscopia, remocion polipos por tecnica de lazo','Extraccion de polipos con asa de polipectomia sin electrocauterio.'),
     ('43255','Endoscopia, control hemorragia','Hemostasia endoscopica mediante clipado, inyeccion o coagulacion.'),
     ('43260','CPRE','Colangiopancreatografia retrograda endoscopica: diagnostico y/o tratamiento de via biliar o pancreatica.'),
     ('43580','Endoscopia con biopsia','Procedimiento endoscopico con toma de biopsias en zonas especificas del tracto digestivo.'),
     ('44180','Laparoscopia quirurgica, enterolisis','Liberacion de adherencias intestinales mediante abordaje laparoscopico.'),
     ('44602','Enterorrafia','Reparacion quirurgica de lesiones en intestino delgado por sutura.'),
     ('44604','Colorrafia','Cierre quirurgico de lesiones en colon mediante sutura.'),
     ('44850','Rafia de Mesenterio','Reparacion de desgarros o lesiones en el mesenterio mediante sutura.'),
     ('44950','Apendicectomia Abierta','Reseccion del apendice por cirugia abierta (laparotomia).'),
     ('44960','Apendicectomia con Apendice Perforado','Cirugia abierta por apendicitis complicada con peritonitis o absceso.'),
     ('44970','Apendicectomia Laparoscopica','Reseccion del apendice mediante tecnica laparoscopica.'),
     ('45378','Colonoscopia diagnostica','Evaluacion endoscopica del colon ante sintomas digestivos.'),
     ('45379','Colonoscopia, extraccion de cuerpo extrano','Retiro de objeto extrano visualizado durante colonoscopia.'),
     ('45382','Colonoscopia con control de sangrado','Ante hemorragia digestiva baja; incluye intervencion para detener el sangrado.'),
     ('45383','Colonoscopia, ablacion tumoral con cauterio','Destruccion de tumores en colon usando calor durante colonoscopia.'),
     ('45385','Colonoscopia, ablacion tumoral por lazo','Reseccion de lesiones colonicas (polipos) con asa de polipectomia.'),
     ('46614','Anoscopia con control de sangrado','Evaluacion y hemostasia de lesiones rectales o anales con sangrado.'),
     ('47562','Colecistectomia abierta','Extraccion de vesicula biliar por cirugia tradicional.'),
     ('49020','Drenaje de absceso peritoneal','Manejo quirurgico de absceso abdominal, excluyendo apendicular.'),
     ('50590','Litotripsia extracorporea','Fragmentacion no invasiva de calculos renales con ondas de choque.'),
     ('52281','Dilatacion uretral','Tratamiento de estenosis uretral, puede ser ambulatorio o en quirofano.'),
     ('52310','Retiro de cateter doble J','Extraccion de endoprotesis ureteral colocada previamente.'),
     ('52332','Cistouretroscopia con colocacion de doble J','Implantacion de ferula para drenaje urinario superior.'),
     ('52351','Cistouretroscopia con ureteroscopia diagnostica','Evaluacion visual del sistema urinario superior e inferior.'),
     ('52352','Litotripsia intracorporea','Fragmentacion de calculos dentro de ureter o vejiga con instrumental.'),
     ('52353','Ureteroscopia con litotripsia + cateterizacion','Procedimiento combinado para litiasis ureteral.'),
     ('52450','Incision transuretral de prostata','Tratamiento de HBP en pacientes con pequeno volumen prostatico.'),
     ('52500','Reseccion cuello vesical','Reseccion anatomica en casos de obstruccion vesical.'),
     ('52601','RTU de prostata completa','Reseccion electroquirurgica de la prostata, estandar en HBP severa.'),
     ('52648','Vaporizacion laser de prostata','Tecnica alternativa a la RTU, util en pacientes con comorbilidades.'),
     ('55250','Vasectomia','Metodo quirurgico de esterilizacion masculina.'),
     ('56303','Fulguracion de quiste de ovario','Cauterizacion de quiste ovarico durante cirugia ginecologica.'),
     ('56304','Lisis de adherencias abdominales','Liberacion quirurgica de sinequias intraabdominales.'),
     ('56340','Colecistectomia laparoscopica','Extraccion vesicular minimamente invasiva, tecnica estandar actual.'),
     ('56342','Colecistectomia laparoscopica + exploracion de via biliar','Indicado cuando se sospecha coledocolitiasis intraoperatoria.'),
     ('56351','Histeroscopia con polipectomia','Reseccion de polipos endometriales bajo vision directa.'),
     ('57452','Colposcopia','Evaluacion del cuello uterino ante alteraciones citologicas.'),
     ('57520','Conizacion con bisturi / laser','Escision diagnostica o terapeutica del cuello uterino.'),
     ('57522','Conizacion por asa (LEEP)','Tecnica mas frecuente para displasias cervicales.'),
     ('58120','Legrado uterino no obstetrico','Reseccion endometrial por sangrado anormal o diagnostico.'),
     ('58140','Miomectomia abdominal','Extraccion de uno o varios miomas por laparotomia.'),
     ('58146','Miomectomia vaginal','Para multiples miomas de gran tamano via vaginal.'),
     ('58150','Histerectomia total abdominal','Extraccion del utero, con o sin anexectomia.'),
     ('58555','Histeroscopia diagnostica','Visualizacion de cavidad endometrial para estudio de patologia.'),
     ('58558','Histeroscopia con biopsia/polipectomia','Procedimiento habitual en sangrado uterino anormal.'),
     ('58561','Histeroscopia con remocion de leiomioma','Tecnica selectiva para miomas submucosos.'),
     ('58563','Histeroscopia con ablacion endometrial','Tratamiento de sangrado uterino cronico refractario.'),
     ('58600','Ligadura de trompas via abdominal/vaginal','Esterilizacion quirurgica electiva.'),
     ('58605','Ligadura postparto','Se realiza tras parto vaginal o cesarea inmediata.'),
     ('58611','Ligadura durante cesarea','Planificacion familiar quirurgica al momento del parto por cesarea.'),
     ('58661','Ligadura de trompas laparoscopica','Esterilizacion femenina mediante cirugia minimamente invasiva.'),
     ('59130','Maternidad (paquete)','Honorarios medicos y hospitalizacion relacionados con la atencion del parto.'),
     ('59400','Parto eutocico','Parto vaginal sin complicaciones, atencion completa.'),
     ('59510','Cesarea multiple','Cesarea realizada por gestacion multiple.'),
     ('59514','Cesarea','Intervencion quirurgica para extraccion fetal por via abdominal.'),
     ('59820','Legrado por aborto diferido o incompleto','Evacuacion uterina tras aborto retenido o parcial.'),
     ('60252','Tiroidectomia total o subtotal para malignidad','Extraccion parcial o total de tiroides con diseccion de cuello limitada.'),
     ('62270','Puncion lumbar terapeutica','Extraccion de LCR o aplicacion de farmacos via espinal.'),
     ('62287','Aspiracion de nucleo pulposo','Tratamiento percutaneo de hernia discal con discectomia parcial.'),
     ('64441','Bloqueo nervios paravertebrales multiples','Manejo del dolor cronico o agudo toracico o lumbar.'),
     ('64470','Bloqueo articular facetas cervical/toracico','Inyeccion anestesica en articulaciones facetarias.'),
     ('64475','Bloqueo faceta lumbar/sacro primer nivel','Tratamiento de dolor lumbar cronico.'),
     ('64476','Bloqueo faceta lumbar/sacro nivel adicional','Bloqueo adicional al anterior, listado por separado.'),
     ('64483','Inyeccion epidural transforaminal lumbar/sacro','Tecnica usada en radiculopatias para alivio de dolor.'),
     ('65756','Implantes de anillos intraestromales','Manejo quirurgico de queratocono en etapas iniciales.'),
     ('65757','Crosslinking corneal','Refuerzo del colageno corneal, indicado en queratocono progresivo.'),
     ('65758','Excimer laser (refractiva)','Cirugia refractiva para correccion de miopia, hipermetropia o astigmatismo.'),
     ('65855','Trabeculotomia laser','Tratamiento de glaucoma mediante apertura del angulo de drenaje.'),
     ('66821','YAG laser','Apertura de capsula posterior en opacidad tras cirugia de catarata.'),
     ('66982','Extraccion de catarata con lente intraocular','Cirugia estandar de catarata con implante de lente.'),
     ('67028','Inyeccion intraocular (Lucentis, Vabysmo)','Aplicacion de antiangiogenicos para degeneracion macular o retinopatia.'),
     ('67228','Fotocoagulacion (laser)','Tratamiento retiniano en diabetes, desprendimientos o desgarros.'),
     ('67800','Chalazion simple','Extraccion de una unica lesion inflamatoria del parpado.'),
     ('67801','Chalazion multiple','Reseccion de multiples chalaziones en un solo procedimiento.'),
     ('67805','Chalazion en ambos parpados','Intervencion en ambos parpados del mismo ojo o en ojos distintos.'),
     ('68320','Pterigio','Reseccion quirurgica de crecimiento fibrovascular en conjuntiva/cornea.'),
     ('79020','Yodoterapia','Tratamiento con yodo radiactivo en patologias tiroideas.'),
     ('90780','Biologicos','Administracion de medicamentos biologicos (inmunoterapia, anticuerpos monoclonales).'),
     ('90937','Hemodialisis','Sesion de hemodialisis para manejo de insuficiencia renal cronica.'),
     ('92982','Angioplastia','Dilatacion de arterias coronarias u otras, con o sin colocacion de stent.'),
     ('93010','Interpretacion de EKG','Lectura medica de electrocardiograma.'),
     ('93230','Holter','Monitoreo ambulatorio continuo del ritmo cardiaco por 24-48 horas.'),
     ('93320','Ecocardiograma (Honorario)','Evaluacion ecografica del corazon; incluye solo honorarios.'),
     ('93452','Cateterismo de corazon izquierdo','Evaluacion invasiva del ventriculo izquierdo y arterias coronarias.'),
     ('93548','Cateterismo cardiaco (Base)','Incluye cateterismo derecho o izquierdo, segun indicacion clinica.'),
     ('93784','MAPA','Monitoreo ambulatorio de presion arterial durante 24 horas.'),
     ('95812','Electroencefalograma','Registro de la actividad electrica cerebral.'),
     ('96450','Quimioterapia intratecal','Administracion de quimioterapia directamente en el LCR.'),
     ('96522','Limpieza de Implantofix','Mantenimiento y desobstruccion de puerto implantado para quimioterapia.'),
     ('96543','Aspiracion de medula osea','Obtencion de muestra de medula osea con fines diagnosticos.'),
     ('96544','Biopsia de hueso','Extraccion de tejido oseo para estudio histopatologico.'),
     ('99141','Anestesia en imagenes','Sedacion moderada durante procedimientos diagnosticos (TAC, RMN).'),
     ('99203','Exceso de honorario','Se factura cuando los honorarios exceden los limites contractuales.'),
     ('99231','Habitacion (visita medica)','Honorario por visita medica diaria en hospitalizacion.'),
     ('99251','Interconsulta','Honorario por evaluacion medica de otra especialidad.'),
     ('99252','Interconsulta subsecuente','Seguimiento de interconsulta previamente iniciada.'),
     ('99281','Emergencia','Honorario por atencion medica en area de emergencia.'),
     ('99285','Emergencia compleja','Evaluacion de mayor complejidad en emergencia (inestabilidad, reanimacion).'),
     ('99291','Intermedios (honorarios)','Visitas medicas diarias en cuidados intermedios.'),
     ('99292','UCI Costa (honorarios)','Visitas medicas diarias en cuidados intensivos.'),
     ('99431','Historia clinica RN','Registro clinico inicial en neonatos con diagnostico.'),
     ('99433','Visita neonatologia','Seguimiento diario por neonatologo.'),
     ('99436','Recepcion y estabilizacion del RN','Atencion al recien nacido tras parto o cesarea.'),
     ('301001','Insumos','Gasto por material medico general: jeringas, apositos, sondas.'),
     ('301002','Medicinas','Gasto por medicamentos administrados durante atencion medica.'),
     ('301018','Medicinas no cubiertas','Farmacos excluidos por el contrato.'),
     ('301019','Insumos no cubiertos','Incluye elementos como refrigerios y material de acompanante.'),
     ('301048','IVA','Codigo contable para detallar el IVA en insumos y servicios.'),
     ('301050','Honorarios no cubiertos','Honorarios medicos no reconocidos por cobertura.'),
     ('301075','IVA no cubierto','Valor de IVA no cubierto por el contrato.'),
     ('301090','Suplementos nutricionales','Formulas y suplementos alimenticios.'),
     ('370002','Hemocomponentes','Transfusion de sangre y sus derivados.'),
     ('397050','Ambulancia terrestre','Transporte medico terrestre del paciente.'),
     ('401000','Radiografia','Radiografias de cualquier area anatomica.'),
     ('504001','Laboratorio','Gastos por examenes de laboratorio.'),
     ('507009','Plaquetoferesis','Procedimiento de plaquetoferesis.'),
     ('601011','Cinecoronariografia','Codigo base; el resto de codigos se evalua segun protocolo.'),
     ('601052','Eco stress','Prueba de esfuerzo con ecocardiograma.'),
     ('602000','Tomografia','Tomografias de cualquier area anatomica.'),
     ('603000','Ecografia','Ecografias de cualquier area anatomica.'),
     ('604001','Ecocardiograma','Liquidacion de ecocardiogramas.'),
     ('605000','Resonancia','Resonancias magneticas de cualquier area anatomica.'),
     ('606026','PET Scan (fuera del pais)','Tomografia por emision de positrones; estudio funcional avanzado en oncologia.'),
     ('607009','Densitometria osea','Medicion de densidad mineral osea; osteoporosis o riesgo de fractura.'),
     ('801000','Paquetes','Procedimiento negociado como paquete; incluye estudios e insumos.'),
     ('801001','Cuarto y alimento','Gasto de estadia (habitacion) y alimentos.'),
     ('801005','Quirofano','Uso de quirofano y sala de recuperacion.'),
     ('801011','Administracion y servicios','Administracion de medicinas durante hospitalizacion.'),
     ('801015','UCI Habitacion','Estadia y alimentos en area de UCI.'),
     ('801016','Intermedios','Estadia y alimentos en cuidados intermedios.'),
     ('801017','Uso/Alquiler de Equipos','Uso o alquiler de equipos medicos.'),
     ('801020','Servicios Hospitalarios','Uso de salas, enfermeria, instrumental y otros servicios hospitalarios.'),
     ('810001','Protesis','Protesis cardiovasculares y traumatologicas.'),
     ('810002','IVA protesis al 70%','IVA correspondiente a protesis y material de osteosintesis.'),
     ('810006','Material de Osteosintesis','Barras, clavos y placas para fijacion osea.'),
     ('901010','Terapia fisica','Sesiones de fisioterapia para rehabilitacion funcional o manejo del dolor.'),
     ('902012','Terapia respiratoria','Intervenciones para mejorar la funcion pulmonar.'),
     ('1000122','Anestesiologia en parto','Honorarios por anestesia (epidural o general) durante parto.'),
     ('1000204','Terapia del dolor','Procedimientos para control de dolor agudo o cronico.'),
     ('1000205','Medicamentos hospitalarios terapia del dolor','Farmacos usados durante estancia hospitalaria en manejo del dolor.'),
     ('1000206','Medicamentos ambulatorios terapia del dolor','Farmacos para continuar manejo del dolor fuera del hospital.')
    ) AS v (Codigo, Rubro, Descripcion)
)
INSERT INTO dbo.CatalogoCodigoLiquidacion (Codigo, Rubro, Descripcion)
SELECT s.Codigo, s.Rubro, s.Descripcion
FROM src s
WHERE NOT EXISTS (SELECT 1 FROM dbo.CatalogoCodigoLiquidacion c WHERE c.Codigo = s.Codigo);
GO

/* ---------------------------------------------------------------------------
   Verificacion
   --------------------------------------------------------------------------- */
SELECT 'columnas nuevas' AS Objeto,
       CASE WHEN COL_LENGTH('dbo.DocumentoClasificacion','TipoSoporte') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','EmisorRuc') IS NOT NULL
                 AND COL_LENGTH('dbo.DocumentoClasificacion','NumeroAutorizacion') IS NOT NULL
            THEN 'OK' ELSE 'FALTA' END AS Estado
UNION ALL SELECT 'DocumentoProcedimiento',
       CASE WHEN OBJECT_ID('dbo.DocumentoProcedimiento','U') IS NOT NULL THEN 'OK' ELSE 'FALTA' END
UNION ALL SELECT 'ClasificacionSobre',
       CASE WHEN OBJECT_ID('dbo.ClasificacionSobre','U') IS NOT NULL THEN 'OK' ELSE 'FALTA' END
UNION ALL SELECT 'CatalogoCodigoLiquidacion (filas)',
       CONVERT(varchar(20), (SELECT COUNT(*) FROM dbo.CatalogoCodigoLiquidacion))
UNION ALL SELECT 'indices nuevos (esperados 5)',
       CONVERT(varchar(20), (SELECT COUNT(*) FROM sys.indexes
                             WHERE name IN ('IX_DocumentoClasificacion_EmisorRuc',
                                            'IX_DocumentoClasificacion_TipoSoporte',
                                            'IX_DocumentoProcedimiento_DataFile',
                                            'IX_DocumentoProcedimiento_Cpt',
                                            'UQ_ClasificacionSobre_Vigente')));
GO
