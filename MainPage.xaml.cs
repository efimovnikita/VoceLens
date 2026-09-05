using VoceLens.ViewModels;

namespace VoceLens;

public partial class MainPage : ContentPage
{
	private readonly MainViewModel _viewModel;

	public MainPage(MainViewModel viewModel)
	{
		InitializeComponent();
		_viewModel = viewModel;
		BindingContext = _viewModel;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_viewModel.RefreshPermissionsAndState();
	}

	private void OnApiKeyTextChanged(object? sender, TextChangedEventArgs e)
	{
		if (BindingContext is MainViewModel vm)
		{
			vm.ApiKey = e.NewTextValue ?? string.Empty;
		}
	}
}
