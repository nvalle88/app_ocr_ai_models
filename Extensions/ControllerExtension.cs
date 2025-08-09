namespace app_tramites.Extensions
{
    using Microsoft.AspNetCore.Mvc;
    using System;
    using System.Collections.Generic;
    using System.Text;
    public static class MyController
    {
        /// <summary>
        /// Redirecciona a una Vista en el mismo controller.
        /// </summary>
        /// <param name="controller">controller actual.</param>
        /// <param name="msg">Mensaje que saldrá en la parte superior derecha de la pantalla cuando cargue la Vista a la que se redirecciona.</param>
        /// <param name="viewName">Nombre de la Vista a la que se va a redireccionar, por defecto es a Index.</param>
        /// <returns></returns>
        public static IActionResult RedirectTo(this Controller controller, string msg = null, string viewName = "Index")
        {

            if (!String.IsNullOrEmpty(msg))
                controller.TempData["Mensaje"] = msg;

            return controller.RedirectToAction(viewName);
        }


        /// <summary>
        /// Redirecciona a una Vista en el otro controller.
        /// </summary>
        /// <param name="controller"></param>
        /// <param name="controller">controller al que se desa hacer la redirección</param>
        /// <param name="viewName">Acción a la que se desea redireccionar</param>
        /// <param name="msg">Mensaje que saldrá en la parte superior derecha de la pantalla cuando cargue la Vista a la que se redirecciona.</param>
        /// <returns></returns>
        public static IActionResult RedirectTo(this Controller controller, string controllerName, string viewName, string msg = null)
        {
            if (!String.IsNullOrEmpty(msg))
                controller.TempData["Mensaje"] = msg;

            return controller.RedirectToAction(viewName, controllerName);
        }
        /// <summary>
        /// Redirecciona a una Vista en el otro controller con parámetros.
        /// </summary>
        /// <param name="controller"></param>
        /// <param name="controller">controller al que se desa hacer la redirección</param>
        /// <param name="viewName">Acción a la que se desea redireccionar</param>
        /// <param name="parametros">Parametros que recive la acción que estamos invocando</param>
        /// <param name="msg">Mensaje que saldrá en la parte superior derecha de la pantalla cuando cargue la Vista a la que se redirecciona.</param>
        /// <returns></returns>
        public static IActionResult RedirectTo(this Controller controller, string controllerName, string viewName, object parametros, string msg = null)
        {
            if (!String.IsNullOrEmpty(msg))
                controller.TempData["Mensaje"] = msg;

            return controller.RedirectToAction(viewName, controllerName, parametros);
        }

        /// <summary>
        /// Redirecciona a una Vista en el otro controller sin parámetros y permite controlar el tiempo que dura el mensaje
        /// </summary>
        /// <param name="controller"></param>
        /// <param name="controller">controller al que se desa hacer la redirección</param>
        /// <param name="viewName">Acción a la que se desea redireccionar</param>
        /// <param name="msg">Mensaje que saldrá en la parte superior derecha de la pantalla cuando cargue la Vista a la que se redirecciona.</param>
        /// <returns></returns>
        public static IActionResult RedirectToMesaggeTime(this Controller controller, string controllerName, string viewName, string msg = null)
        {
            if (!String.IsNullOrEmpty(msg))
                controller.TempData["MensajeTimer"] = msg;

            return controller.RedirectToAction(viewName, controllerName);
        }


        /// <summary>
        /// Redirecciona a una Vista en el otro controller con parámetros y permite controlar el tiempo que dura el mensaje
        /// </summary>
        /// <param name="controller"></param>
        /// <param name="controller">controller al que se desa hacer la redirección</param>
        /// <param name="viewName">Acción a la que se desea redireccionar</param>
        /// <param name="parametros">Parametros que recive la acción que estamos invocando</param>
        /// <param name="msg">Mensaje que saldrá en la parte superior derecha de la pantalla cuando cargue la Vista a la que se redirecciona.</param>
        /// <returns></returns>
        public static IActionResult RedirectToMesaggeTime(this Controller controller, string controllerName, string viewName, object parametros, string msg = null)
        {
            if (!String.IsNullOrEmpty(msg))
                controller.TempData["MensajeTimer"] = msg;

            return controller.RedirectToAction(viewName, controllerName, parametros);
        }



    }
}

