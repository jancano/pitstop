#if defined(PITSTOP_SELECT_MODULE)
import selection;
int main() { return selected() == 1 ? 0 : 1; }
#else
int main() { return 0; }
#endif
