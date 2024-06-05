using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration.Install;
using System.Linq;
using System.ServiceProcess;
using System.Threading.Tasks;

namespace SyncChanges.Service
{
    [RunInstaller(true)]
    public partial class ProjectInstaller : System.Configuration.Install.Installer
    {
        public ProjectInstaller()
        {
            ServiceProcessInstaller processInstaller = new ServiceProcessInstaller();
            ServiceInstaller serviceInstaller = new ServiceInstaller();

            // Sử dụng tài khoản LocalSystem
            processInstaller.Account = ServiceAccount.LocalService;

            // Bạn có thể sử dụng các tùy chọn khác như:
            // processInstaller.Account = ServiceAccount.LocalService;
            // processInstaller.Account = ServiceAccount.NetworkService;

            // Thiết lập thông số dịch vụ
            serviceInstaller.StartType = ServiceStartMode.Automatic;
            serviceInstaller.ServiceName = "SyncChanges.Service";

            // Thêm các installer vào collection
            Installers.Add(processInstaller);
            Installers.Add(serviceInstaller);
         //   InitializeComponent();
           
        }
    }
}
