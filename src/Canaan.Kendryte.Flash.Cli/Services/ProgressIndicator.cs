using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using AaronLuna.ConsoleProgressBar;

namespace Canaan.Kendryte.Flash.Cli.Services
{
    internal class ProgressIndicator
    {
        private readonly ConsoleProgressBar _progressBar;
        private JobItemStatus _jobItem;

        public ProgressIndicator()
        {
            _progressBar = new ConsoleProgressBar();
        }

        public void SetJobItem(JobItemType itemType, JobItemStatus jobItem)
        {
            if(_jobItem != null)
                _jobItem.PropertyChanged -= JobItem_PropertyChanged;

            _jobItem = jobItem;
            jobItem.PropertyChanged += JobItem_PropertyChanged;
            _progressBar.UpdateText(itemType.ToString());
        }

        private void JobItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(JobItemStatus.Progress):
                    _progressBar.Report(_jobItem.Progress);
                    break;
                default:
                    break;
            }
        }
    }
}
