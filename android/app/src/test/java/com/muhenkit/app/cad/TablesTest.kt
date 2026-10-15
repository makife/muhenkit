package com.muhenkit.app.cad

import org.junit.Assert.assertEquals
import org.junit.Test
import java.io.File

/** Tablo üreticileri: hesap doğruluğu + DXF ihracı (ezdxf denetimi için build/tables). */
class TablesTest {
    private val salon = ProjectTables.Room("SALON", "5,9", "3,9", "3", "0.85", "50", "80-50-30", eta = "0,36", power = "40", flux = "1800", type = "V")

    @Test
    fun roomIndexMatchesSampleProject() {
        // örnek projede SALON: k = 1,09
        assertEquals(1.09, ProjectTables.roomIndex(salon)!!, 0.005)
        // Z = 50·5,9·3,9·1,25 / (1800·0,36) = 2,22
        assertEquals(2.22, ProjectTables.lampCount(salon, null)!!, 0.01)
        assertEquals("3", ProjectTables.resolved(salon).last())
    }

    @Test
    fun numberFormatKeepsIntegerZeros() {
        assertEquals("9500", ProjectTables.f(9500.0, 0))
        assertEquals("60", ProjectTables.f(60.0, 0))
        assertEquals("1,5", ProjectTables.f(1.50))
        assertEquals("100", ProjectTables.f(100.0))
        assertEquals("0,36", ProjectTables.f(0.36))
    }

    @Test
    fun exportTables() {
        val dir = File("build/tables").apply { mkdirs() }
        val rooms = listOf(salon, salon.copy(name = "Ç. ODASI", a = "3,2", b = "3", lux = "125", eta = ""), salon.copy(name = "MUTFAK", a = "2,6", b = "4,2", lux = "125", eta = "", zUsed = "2"))
        val tables = listOf(
            ProjectTables.lightingTable(10.0, rooms, "Not: Tasarruflu ampül kullanılması tercih edilerek, armatür seçimi yapılmıştır."),
            ProjectTables.powerTable(10.0, "TOPLAM GÜÇ TABLOSU", listOf(ProjectTables.Load("1. KAT DAİRE", "9500", "60"), ProjectTables.Load("2. KAT DAİRE", "9500", "60"), ProjectTables.Load("MERDİVEN", "180", "100"))),
            ProjectTables.titleBlock(10.0, ProjectTables.TITLE_FIELDS.associate { (k, lv) -> k to lv.second.ifEmpty { lv.first.uppercase() } } + mapOf("firma" to "TESLA MÜHENDİSLİK", "kurulu" to "19180", "talep" to "11580")),
        )
        tables.forEachIndexed { i, t ->
            val s = t.dxfSnippet().replace('\n', '\u001f')
            File(dir, "T$i.txt").writeText("@SYM\tT$i\t${t.name}\tTablo\t0\t0\nS\t$s\n@END\n")
        }
    }
}
