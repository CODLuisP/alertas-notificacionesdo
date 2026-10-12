-- Visitas que terminaron porque se desvinculó el vehículo de la geocerca estando adentro.
-- El servidor de alertas lo reporta como geofenceExit aunque el vehículo no se movió;
-- el microservicio ahora lo detecta (posición todavía dentro de la forma) y lo marca aquí.
--
-- Ejecutar ANTES de desplegar la nueva versión del microservicio: el INSERT de visitas
-- usa esta columna y fallaría si no existe.

ALTER TABLE visitas_geocerca
    ADD COLUMN salida_por_desvinculacion TINYINT(1) NOT NULL DEFAULT 0 AFTER entrada_incompleta;

-- Corrección del histórico: única visita existente en ese caso
-- (312-daj135 en "Prueba", 11/10/2026 12:22 hora de Lima; estaba detenido dentro de la geocerca).
UPDATE visitas_geocerca
SET salida_por_desvinculacion = 1
WHERE id = 3 AND deviceID = '312-daj135' AND geofenceID = 17;
