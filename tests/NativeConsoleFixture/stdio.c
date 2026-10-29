#include <stdio.h>
#include <io.h>
#include <windows.h>

/* Native CRT startup must initialize stdout, as it does in a native miner. */
int main(void)
{
    printf("NATIVE_CRT_TTY=%d\n", _isatty(_fileno(stdout)));
    printf("GPU0[t0] share accepted [100ms] [fixture][0] LIVE_CRT_FIXTURE\n");
    /* A redirected stream would remain buffered until exit; do not fflush. */
    Sleep(5000);
    return 0;
}
