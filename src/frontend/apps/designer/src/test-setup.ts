// SPDX-License-Identifier: AGPL-3.0-only
// Tests run in parallel with the other layers (bin/test-all.sh); on a busy machine findBy and waitFor get 3 s, not 1 s.
import { configure } from "@testing-library/react";

configure({ asyncUtilTimeout: 3000 });
