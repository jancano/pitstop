{{#modules}}#include <cstdio>
import {{id:projectName}};
int main() {
    std::printf("Pitstop template %d\n", module_answer());
    return module_answer() == 42 ? 0 : 1;
}
{{/modules}}{{^modules}}#include "answer.hpp"
#include <cstdio>

int main() {
    std::printf("Pitstop template %d\n", answer());
    return answer() == 42 ? 0 : 1;
}
{{/modules}}
