namespace __PROJECT_NAMESPACE__.Operations;

public static class UnsealMaterialProviderFactory
{
    public static IUnsealMaterialProvider Create() =>
        new MultiPassphraseUnsealProvider();
}
