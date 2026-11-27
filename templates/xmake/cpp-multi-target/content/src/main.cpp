{{#stdModule}}import std;
{{/stdModule}}{{^stdModule}}#include <cstdio>
{{/stdModule}}
import {{id:projectName}};

int main() {
{{#stdModule}}    std::cout << "Pitstop template " << answer() << '\n';
{{/stdModule}}{{^stdModule}}    std::printf("Pitstop template %d\n", answer());
{{/stdModule}}
    return answer() == 42 ? 0 : 1;
}
