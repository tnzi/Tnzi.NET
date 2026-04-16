import type { MockScenario } from '../types'
import s01 from './01-simple-chat'
import s02 from './02-deep-reasoning'
import s03 from './03-rag-lookup'
import s04 from './04-artifact-todo'
import s05 from './05-workflow-trip'
import s06 from './06-skill-weather'
import s07 from './07-multi-agent'
import s08 from './08-attachments'
import s09 from './09-message-branch'
import s10 from './10-embed-widget'

export const allScenarios: readonly MockScenario[] = [s01, s02, s03, s04, s05, s06, s07, s08, s09, s10]
