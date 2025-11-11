 //javascript wwwroot/js/global.js
 // Configuración global de SweetAlert2 (opcional)
 const Toast = Swal.mixin({
     toast: true,
     position: 'top-end',
     showConfirmButton: false,
     timer: 3000,
     timerProgressBar: true,
     didOpen: (toast) => {
         toast.addEventListener('mouseenter', Swal.stopTimer);
         toast.addEventListener('mouseleave', Swal.resumeTimer);
     }
 });

 // Exportar para uso global
 window.Toast = Toast;
window.Swal = Swal;
