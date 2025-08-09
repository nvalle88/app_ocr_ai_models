namespace app_ocr_ai_models.Utils
{
    public static class Mensaje
    {
        
        public static string Satisfactory { get { return "The action has been successfully completed."; } }
        public static string ErrorListado { get { return "Ha ocurrido un error al cargar el listado."; } }
        public static string Excepcion { get { return "An exception has occurred."; } }
        public static string ErrorLoadData { get { return "An error occurred while loading the data"; } }
        public static string ErrorUploadFiles { get { return "Ha ocurrido un error al subir la documentaci�n adicional."; } }

        public static string MessaggeOK { get { return "Satisfactorio"; } }

        public static string RecordNotFound { get { return "The requested record has not been found"; } }
        public static string BorradoNoSatisfactorio { get { return "No es posible eliminar el registro, existen relaciones que dependen de �l"; } }
        public static string Error { get { return "Error"; } }



        public static string FormulaNominaInvalida { get { return "La f�rmula tiene alg�n error.Por favor verifique nuevamente."; } }

        public static string NoPuedeEditarEspecificacion { get { return "No se puede editar la especificaci�n, existen Productos o Normas que lo utilizan, debe eliminar esta especificaci�n de los Productos o las Normas."; } }

        public static string FaltaIngresoDatos { get { return "Debe completar el formulario"; } }
        public static string NoExisteModulo { get { return "No se ha encontrado el M�dulo"; } }
        public static string Obligatorio { get { return "Debe introducir datos en el campo"; } }
        public static string FechaRangoMenor { get { return "La fecha inicial no puede ser mayor que la fecha final "; } }
        public static string FechaRangoMayor { get { return "La fecha final no puede ser menor que la fecha inicial "; } }
        public static string ExisteRegistro { get { return "Existe un registro de igual informaci�n"; } }

        public static string ValorMinimoMayorRangoMaximo { get { return "El valor m�nimo no puede ser mayor que el valor m�ximo"; } }
        public static string RangoMinimoMayorRangoMaximo { get { return "El rango m�nimo no puede ser mayor que el rango m�ximo"; } }
        public static string DebeIntroducirAlMenosUnRango { get { return "Debe introducir al menos un valor de rango."; } }
        public static string ExisteEmpleado { get { return "Existe un empleado de igual informaci�n"; } }

        public static string RecordExists { get { return "The record already exists, please try again with another code."; } }
        public static string NoExistenRegistrosPorAsignar { get { return "No existen Registros por agregar"; } }
        public static string GenerandoListas { get { return "Las listas se est�n cargando"; } }
        public static string GuardadoSatisfactorio { get { return "Los datos se han guardado correctamente"; } }
        public static string BorradoSatisfactorio { get { return "El registro se ha eliminado correctamente"; } }
        public static string ErrorFichaEdicion { get { return "Existe una ficha en edici�n"; } }
        public static string ErrorCargaArchivo { get { return "Se produjo un error al cargar el archivo"; } }
        public static string ErrorServicio { get { return "No se pudo establecer conexi�n con el servicio"; } }
        public static string FixForm { get { return "Some information is incorrect. Please review and correct it."; } }
        public static string SinArchivo { get { return "No existe archivo para descargar"; } }
        public static string RegistroEditado { get { return "El registro se ha editado corectamente"; } }

        public static string RegistroNoExiste { get { return "El registro que desea editar no existe."; } }
        public static string ErrorCrear { get { return "Ha ocurrido un error al crear el registro."; } }
        public static string ErrorEditar { get { return "Ha ocurrido un error al editar el registro."; } }
        public static string ErrorEliminar { get { return "Ha ocurrido un error al eliminar el registro."; } }
        public static string Information { get { return "Informaci�n"; } }
        public static string Warning { get { return "Aviso"; } }
        public static string Success { get { return "Success"; } }

        public static string ErrorActivar { get { return "Ha ocurrido un error al activar el registro."; } }
        public static string ErrorDesactivar { get { return "Ha ocurrido un error al desactivar el registro."; } }

        public static string SeleccioneIndice { get { return "Debe seleccionar un �ndice en: situaci�n propuesta."; } }

        public static string ConceptoNoExiste { get { return "El concepto no existe."; } }
        public static string EmpleadoNoExiste { get { return "Identificaci�n del empleado no existe."; } }
        public static string ConceptoEmpleadoNoExiste { get { return "El concepto y la Identificaci�n del empleado no existen."; } }

        public static object SeleccionarFichero { get { return "Debe seleccionar un fichero..."; } }

        public static object ReportadoConErrores { get { return "Verifique la informaci�n del los reportados cargados ya que existen errores en su informaci�n, la informaci�n con errores no fue guardada..."; } }
        public static object ReportadoNoCumpleFormato { get { return "Verifique el formato del archivo seleccionado,Nota: Debe seleccionar un archivo Excel(.xlsx).El cual debe contener el orden de las columnas de la siguiente distribuci�n...1:C�digo del concepto, 2:Identificaci�n del empleado, 3:Nombre y Apellidos del empleado, 4:Cantidad, 5:Importe"; } }
        public static string CarpetaDocumento { get { return "CertificadosCalidad"; } }
        public static string NoExistenRegistros { get { return "No existen registros para mostrar"; } }

        public static string ErrorFechaDesdeHasta { get { return "La fecha desde debe ser menor que la fecha hasta."; } }
        public static string AccesoNoAutorizado { get { return "No tiene los permisos necesarios para acceder a este sitio"; } }

        public static object NoCumpleNorma { get { return "La especificaci�n isertado no cumple norma."; } }

        public static string DebeSeleccionarOrdenes { get { return "Debe seleccionar al menos un n�mero de orden."; } }

        public static object NoPuedeEditar { get; internal set; }

        public static string CarpertaHost { get; set; }
        public static string AsuntoCorreo { get; set; }


    }
}
